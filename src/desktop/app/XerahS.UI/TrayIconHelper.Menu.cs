#region License Information (GPL v3)

/*
    XerahS - The Avalonia UI implementation of ShareX
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.Input;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Hotkeys;
using XerahS.Platform.Abstractions;
using XerahS.UI.Helpers;
using XerahS.UI.Services;
using XerahS.Uploaders.PluginSystem;

namespace XerahS.UI;

/// <summary>ShareX's tray menu (MainMenuBuilder.BuildTrayMenu), with XerahS's jobs, settings, and destinations.</summary>
public partial class TrayIconHelper
{
    internal const string CaptureMenuHeader = "Capture";

    // ShareX's order of the after capture tasks; "Copy OCR text to clipboard" is XerahS's own.
    private static readonly AfterCaptureTasks[] AfterCaptureTaskOrder =
    [
        AfterCaptureTasks.ShowQuickTaskMenu, AfterCaptureTasks.ShowAfterCaptureWindow, AfterCaptureTasks.BeautifyImage,
        AfterCaptureTasks.AddImageEffects, AfterCaptureTasks.AnnotateMedia, AfterCaptureTasks.CopyImageToClipboard,
        AfterCaptureTasks.PinToScreen, AfterCaptureTasks.SendImageToPrinter, AfterCaptureTasks.SaveImageToFile,
        AfterCaptureTasks.SaveImageToFileWithDialog, AfterCaptureTasks.SaveThumbnailImageToFile, AfterCaptureTasks.PerformActions,
        AfterCaptureTasks.CopyFileToClipboard, AfterCaptureTasks.CopyFilePathToClipboard, AfterCaptureTasks.CopyFolderPathToClipboard,
        AfterCaptureTasks.ShowInExplorer, AfterCaptureTasks.AnalyzeImage, AfterCaptureTasks.ScanQRCode, AfterCaptureTasks.DoOCR,
        AfterCaptureTasks.CopyOcrTextToClipboard, AfterCaptureTasks.ShowBeforeUploadWindow, AfterCaptureTasks.UploadImageToHost,
        AfterCaptureTasks.DeleteFile
    ];

    private static readonly (AfterUploadTasks Task, string Header)[] AfterUploadTaskOptions =
    [
        (AfterUploadTasks.ShowAfterUploadWindow, "Show \"After upload\" window"),
        (AfterUploadTasks.UseURLShortener, "Shorten URL"),
        (AfterUploadTasks.ShareURL, "Share URL"),
        (AfterUploadTasks.CopyURLToClipboard, "Copy URL to clipboard"),
        (AfterUploadTasks.OpenURL, "Open URL"),
        (AfterUploadTasks.ShowQRCode, "Show QR code window")
    ];

    private static readonly (UploaderCategory Category, string HeaderFormat)[] DestinationCategories =
    [
        (UploaderCategory.Image, "Image uploader: {0}"),
        (UploaderCategory.Text, "Text uploader: {0}"),
        (UploaderCategory.File, "File uploader: {0}"),
        (UploaderCategory.UrlShortener, "URL shortener: {0}"),
        (UploaderCategory.UrlSharing, "URL sharing service: {0}")
    ];

    private IWorkflowOrchestrator? _workflowOrchestrator;
    private NativeMenu? _windowMenu;
    private NativeMenu? _monitorMenu;
    private string _windowMenuSignature = string.Empty;
    private Avalonia.Threading.DispatcherTimer? _windowListTimer;

    /// <summary>Adds ShareX's tray menu, from its Capture submenu to Exit.</summary>
    private void AddShareXMenuItems(IList<NativeMenuItemBase> items)
    {
        bool uploadsEnabled = Core.TaskHelpers.IsUploadAllowed();
        TaskSettings defaults = SettingsManager.DefaultTaskSettings;

        items.Add(Parent(CaptureMenuHeader, BuildCaptureMenu(defaults)));
        if (uploadsEnabled) items.Add(Parent("Upload", BuildUploadMenu()));
        items.Add(Parent("Workflows", BuildWorkflowsMenu()));
        items.Add(Parent("Tools", BuildToolsMenu()));
        items.Add(new NativeMenuItemSeparator());
        items.Add(Parent("After capture tasks", BuildAfterCaptureMenu(defaults)));
        if (uploadsEnabled)
        {
            items.Add(Parent("After upload tasks", BuildAfterUploadMenu(defaults)));
            items.Add(Parent("Destinations", BuildDestinationsMenu(defaults)));
        }
        items.Add(new NativeMenuItemSeparator());
        items.Add(Item("Application settings...", () => NavigateMainWindow("Settings_App")));
        items.Add(Item("Task settings...", () => NavigateMainWindow("Settings_Task")));
        // XerahS edits hotkeys on its Workflows page.
        items.Add(Item("Hotkey settings...", () => NavigateMainWindow("Workflows")));
        items.Add(Item(SettingsManager.Settings.DisableHotkeys ? "Enable hotkeys" : "Disable hotkeys",
            () => RunJob(WorkflowType.DisableHotkeys)));
        // Custom uploaders are on XerahS's Destination settings page; it has no separate window for them.
        if (uploadsEnabled) items.Add(Item("Destination settings...", () => NavigateMainWindow("Settings_Dest")));
        items.Add(new NativeMenuItemSeparator());
        items.Add(Item("Screenshots folder...", () => RunJob(WorkflowType.OpenScreenshotsFolder)));
        items.Add(Item("History...", () => RunJob(WorkflowType.OpenHistory)));
        items.Add(Item("Image history...", () => RunJob(WorkflowType.OpenImageHistory)));
        items.Add(new NativeMenuItemSeparator());
        if (SettingsManager.Settings.RecentTasksSave && SettingsManager.Settings.RecentTasksShowInTrayMenu &&
            SettingsManager.RecentTaskManager.Tasks.Count > 0)
        {
            items.Add(Parent("Recent items", BuildRecentItemsMenu()));
        }
        items.Add(Item("Actions toolbar", () => RunJob(WorkflowType.ToggleActionsToolbar)));
        items.Add(Item($"Show {AppResources.AppName}", OpenMainWindow));
        items.Add(Item("Exit", Exit));
    }

    private NativeMenu BuildCaptureMenu(TaskSettings defaults)
    {
        _windowMenu = new NativeMenu();
        _monitorMenu = new NativeMenu();
        UpdateWindowAndMonitorMenus(force: true);

        var menu = new NativeMenu();
        menu.Items.Add(Item("Fullscreen", () => RunJob(WorkflowType.PrintScreen)));
        menu.Items.Add(new NativeMenuItem("Window") { Menu = _windowMenu });
        menu.Items.Add(new NativeMenuItem("Monitor") { Menu = _monitorMenu });
        menu.Items.Add(Item("Region...", () => RunJob(WorkflowType.RectangleRegion)));
        menu.Items.Add(Item("Last region", () => RunJob(WorkflowType.LastRegion)));
        menu.Items.Add(Item("Screen recording...", () => RunJob(WorkflowType.ScreenRecorder)));
        menu.Items.Add(Item("Screen recording (GIF)...", () => RunJob(WorkflowType.ScreenRecorderGIF)));
        menu.Items.Add(Item("Scrolling capture...", () => RunJob(WorkflowType.ScrollingCapture)));
        menu.Items.Add(Item("Auto capture...", () => RunJob(WorkflowType.AutoCapture)));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(CheckItem("Show cursor", defaults.CaptureSettings.ShowCursor, MenuItemToggleType.CheckBox, () =>
            ChangeDefaultTaskSettings(settings => settings.CaptureSettings.ShowCursor = !settings.CaptureSettings.ShowCursor)));

        var delayMenu = new NativeMenu();
        decimal currentDelay = defaults.CaptureSettings.ScreenshotDelay;
        for (int delay = 0; delay <= 5; delay++)
        {
            int value = delay;
            delayMenu.Items.Add(CheckItem($"Screenshot delay: {value}s", Math.Abs(currentDelay - value) < 0.01m, MenuItemToggleType.Radio,
                () => ChangeDefaultTaskSettings(settings => settings.CaptureSettings.ScreenshotDelay = value)));
        }
        menu.Items.Add(Parent($"Screenshot delay: {currentDelay:0.#}s", delayMenu));
        return menu;
    }

    /// <summary>
    /// Fills the Window and Monitor submenus. The desktop shows the tray menu itself and does not tell XerahS
    /// when it opens, so a timer keeps the lists current; they are rebuilt only when something changed.
    /// </summary>
    private void UpdateWindowAndMonitorMenus(bool force = false)
    {
        if (_windowMenu == null || _monitorMenu == null || !PlatformServices.IsInitialized)
        {
            return;
        }

        WindowInfo[] windows = [];
        ScreenInfo[] screens = [];
        try
        {
            windows = GetCapturableWindows();
            screens = PlatformServices.Screen.GetAllScreens();
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Tray: unable to list the windows and monitors");
        }

        string signature = string.Join('\n', windows.Select(w => $"{w.Handle}:{w.Title}")) + "\n|" +
                           string.Join('\n', screens.Select(s => s.Bounds.ToString()));
        if (!force && signature == _windowMenuSignature)
        {
            return;
        }
        _windowMenuSignature = signature;

        _windowMenu.Items.Clear();
        foreach (WindowInfo window in windows)
        {
            IntPtr handle = window.Handle;
            var item = Item(window.Title.Truncate(50, "..."), () => RunJob(WorkflowType.CustomWindow, settings => settings.CaptureWindowHandle = handle));
            item.Icon = InspectWindowService.GetWindowIcon(handle);
            _windowMenu.Items.Add(item);
        }
        if (_windowMenu.Items.Count == 0)
        {
            _windowMenu.Items.Add(new NativeMenuItem("No windows found") { IsEnabled = false });
        }

        _monitorMenu.Items.Clear();
        for (int i = 0; i < screens.Length; i++)
        {
            System.Drawing.Rectangle bounds = screens[i].Bounds;
            _monitorMenu.Items.Add(Item($"{i + 1}. {bounds.Width}x{bounds.Height}",
                () => RunJob(WorkflowType.CustomRegion, settings => settings.CaptureSettings.CaptureCustomRegion = bounds)));
        }
    }

    /// <summary>ShareX's visible window list (WindowsList.GetVisibleWindowsList): shown windows with a title.</summary>
    private static WindowInfo[] GetCapturableWindows() =>
        PlatformServices.Window.GetAllWindows()
            .Where(w => w.IsVisible && !w.IsMinimized && !string.IsNullOrWhiteSpace(w.Title) && w.Bounds.Width > 0 && w.Bounds.Height > 0)
            .ToArray();

    private void StartWindowListTimer()
    {
        if (_windowListTimer != null)
        {
            return;
        }

        _windowListTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _windowListTimer.Tick += (_, _) =>
        {
            if (ShowTray) UpdateWindowAndMonitorMenus();
        };
        _windowListTimer.Start();
    }

    private NativeMenu BuildUploadMenu()
    {
        var menu = new NativeMenu();
        menu.Items.Add(Item("Upload file...", () => RunJob(WorkflowType.FileUpload)));
        menu.Items.Add(Item("Upload folder...", () => RunJob(WorkflowType.FolderUpload)));
        menu.Items.Add(Item("Upload clipboard...", () => RunJob(WorkflowType.ClipboardUpload)));
        menu.Items.Add(Item("Upload text...", () => RunJob(WorkflowType.UploadText)));
        menu.Items.Add(Item("Upload URL...", () => RunJob(WorkflowType.UploadURL)));
        menu.Items.Add(Item("Drag and drop upload...", () => RunJob(WorkflowType.DragDropUpload)));
        menu.Items.Add(Item("Shorten URL...", () => RunJob(WorkflowType.ShortenURL)));
        return menu;
    }

    private NativeMenu BuildWorkflowsMenu()
    {
        var menu = new NativeMenu();
        foreach (WorkflowSettings workflow in SettingsManager.WorkflowsConfig?.Hotkeys ?? [])
        {
            TaskSettings? taskSettings = workflow.TaskSettings;
            if (taskSettings == null || workflow.Job == WorkflowType.None ||
                (SettingsManager.Settings.WorkflowsOnlyShowEdited && taskSettings.IsUsingDefaultSettings))
            {
                continue;
            }

            string title = taskSettings + (taskSettings.IsUsingDefaultSettings ? string.Empty : "*");
            if (workflow.HotkeyInfo?.IsValid == true)
            {
                title += $"    {workflow.HotkeyInfo}";
            }

            WorkflowSettings selected = workflow;
            menu.Items.Add(Item(title, () => RunWorkflow(selected)));
        }
        return menu;
    }

    private NativeMenu BuildToolsMenu()
    {
        // A native menu has no grid, so each of ShareX's tool categories is a titled group.
        var menu = new NativeMenu();
        foreach (var (category, tools) in ToolMenuCatalog.GetVisibleCategories())
        {
            if (menu.Items.Count > 0) menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(new NativeMenuItem(category.Header) { IsEnabled = false });
            foreach (ToolMenuItem tool in tools)
            {
                string tag = ToolMenuCatalog.GetLaunchTag(tool);
                menu.Items.Add(Item(tool.Header, () => OpenTool(tag)));
            }
        }
        return menu;
    }

    private NativeMenu BuildAfterCaptureMenu(TaskSettings defaults)
    {
        var menu = new NativeMenu();
        bool uploadsEnabled = Core.TaskHelpers.IsUploadAllowed();
        foreach (AfterCaptureTasks task in AfterCaptureTaskOrder)
        {
            if (!uploadsEnabled && task is AfterCaptureTasks.ShowBeforeUploadWindow or AfterCaptureTasks.UploadImageToHost)
            {
                continue;
            }

            AfterCaptureTasks flag = task;
            // AnnotateMedia shares its value with the obsolete AnnotateImage name, which has no description.
            string header = task == AfterCaptureTasks.AnnotateMedia ? "Annotate media" : EnumExtensions.GetDescription(task);
            menu.Items.Add(CheckItem(header, defaults.AfterCaptureJob.HasFlag(task), MenuItemToggleType.CheckBox,
                () => ChangeDefaultTaskSettings(settings => settings.AfterCaptureJob ^= flag)));

            // ShareX opens the presets from "Add image effects" itself; a native menu item cannot be both
            // a check box and a submenu, so they follow it.
            if (task == AfterCaptureTasks.AddImageEffects)
            {
                menu.Items.Add(BuildImageEffectPresetMenu(defaults));
            }
        }
        return menu;
    }

    private NativeMenuItem BuildImageEffectPresetMenu(TaskSettings defaults)
    {
        var presetMenu = new NativeMenu();
        var presets = defaults.ImageSettings.ImageEffectPresets ?? [];
        int selected = defaults.ImageSettings.SelectedImageEffectPreset;
        for (int i = 0; i < presets.Count; i++)
        {
            if (presets[i] == null) continue;
            int index = i;
            presetMenu.Items.Add(CheckItem(presets[i].ToString(), index == selected, MenuItemToggleType.Radio,
                () => ChangeDefaultTaskSettings(settings => settings.ImageSettings.SelectedImageEffectPreset = index)));
        }
        if (presetMenu.Items.Count == 0)
        {
            presetMenu.Items.Add(new NativeMenuItem("No image effect presets") { IsEnabled = false });
        }

        string? selectedName = selected >= 0 && selected < presets.Count ? presets[selected]?.ToString() : null;
        return Parent(string.IsNullOrEmpty(selectedName) ? "Image effect presets" : $"Image effect preset: {selectedName}", presetMenu);
    }

    private NativeMenu BuildAfterUploadMenu(TaskSettings defaults)
    {
        var menu = new NativeMenu();
        foreach (var (task, header) in AfterUploadTaskOptions)
        {
            AfterUploadTasks flag = task;
            menu.Items.Add(CheckItem(header, defaults.AfterUploadJob.HasFlag(task), MenuItemToggleType.CheckBox,
                () => ChangeDefaultTaskSettings(settings => settings.AfterUploadJob ^= flag)));
        }
        return menu;
    }

    /// <summary>
    /// ShareX chooses the default task settings' image, text, and file uploaders, URL shortener, and URL sharing
    /// service here. In XerahS each is the default uploader instance of its category.
    /// </summary>
    private NativeMenu BuildDestinationsMenu(TaskSettings defaults)
    {
        var menu = new NativeMenu();
        foreach (var (category, headerFormat) in DestinationCategories)
        {
            List<UploaderInstance> instances = InstanceManager.Instance.GetInstancesByCategory(category);
            string? currentId = GetDefaultDestinationId(defaults, category);
            var instanceMenu = new NativeMenu();
            foreach (UploaderInstance instance in instances)
            {
                UploaderCategory selectedCategory = category;
                string instanceId = instance.InstanceId;
                instanceMenu.Items.Add(CheckItem(instance.DisplayName, string.Equals(instanceId, currentId, StringComparison.OrdinalIgnoreCase),
                    MenuItemToggleType.Radio, () => SetDefaultDestination(selectedCategory, instanceId)));
            }
            if (instanceMenu.Items.Count == 0)
            {
                instanceMenu.Items.Add(new NativeMenuItem("No destinations") { IsEnabled = false });
            }

            string currentName = instances.FirstOrDefault(i => string.Equals(i.InstanceId, currentId, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? "None";
            menu.Items.Add(Parent(string.Format(headerFormat, currentName), instanceMenu));
        }
        return menu;
    }

    private static string? GetDefaultDestinationId(TaskSettings defaults, UploaderCategory category)
    {
        string? taskInstanceId = category switch
        {
            UploaderCategory.UrlShortener => defaults.UrlShortenerDestinationInstanceId,
            UploaderCategory.UrlSharing => defaults.UrlSharingDestinationInstanceId,
            _ => defaults.DestinationInstanceId
        };

        // The default task settings' own destination counts only for the category it belongs to.
        if (!string.IsNullOrEmpty(taskInstanceId) && InstanceManager.Instance.GetInstance(taskInstanceId)?.Category == category)
        {
            return taskInstanceId;
        }

        return InstanceManager.Instance.GetDefaultInstance(category)?.InstanceId;
    }

    /// <summary>Makes an instance its category's default, as the Destination settings page and the uploader import do.</summary>
    private void SetDefaultDestination(UploaderCategory category, string instanceId)
    {
        try
        {
            InstanceManager.Instance.SetDefaultInstance(category, instanceId);
            ChangeDefaultTaskSettings(settings =>
            {
                switch (category)
                {
                    case UploaderCategory.UrlShortener: settings.UrlShortenerDestinationInstanceId = instanceId; break;
                    case UploaderCategory.UrlSharing: settings.UrlSharingDestinationInstanceId = instanceId; break;
                    default: settings.DestinationInstanceId = null; break;
                }
            });
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Tray: unable to change the destination");
        }
    }

    private NativeMenu BuildRecentItemsMenu()
    {
        var menu = new NativeMenu();
        foreach (RecentTask task in SettingsManager.RecentTaskManager.GetTasks(SettingsManager.Settings.RecentTasksTrayMenuMostRecentFirst))
        {
            RecentTask recentTask = task;
            var taskMenu = new NativeMenu();
            taskMenu.Items.Add(Item("Copy", () => CopyRecentTask(recentTask)));
            taskMenu.Items.Add(Item("Open", () => OpenRecentTask(recentTask)));
            menu.Items.Add(Parent(task.TrayMenuText, taskMenu));
        }
        return menu;
    }

    private static void CopyRecentTask(RecentTask task)
    {
        string text = task.ToString();
        if (!string.IsNullOrEmpty(text))
        {
            PlatformServices.Clipboard.SetText(text);
        }
    }

    /// <summary>ShareX's RecentTask.Open: the shortened URL, else the URL, else the file.</summary>
    private static void OpenRecentTask(RecentTask task)
    {
        string url = !string.IsNullOrEmpty(task.ShortenedURL) ? task.ShortenedURL : task.URL;
        if (!string.IsNullOrEmpty(url))
        {
            PlatformServices.System.OpenUrl(url);
        }
        else if (!string.IsNullOrEmpty(task.FilePath))
        {
            PlatformServices.System.OpenFile(task.FilePath);
        }
    }

    private void OnRecentTasksChanged(object? sender, EventArgs e) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(BuildTrayMenu);

    /// <summary>Changes the default task settings, as ShareX's tray toggles do, and saves them.</summary>
    private void ChangeDefaultTaskSettings(Action<TaskSettings> change)
    {
        change(SettingsManager.DefaultTaskSettings);
        BuildTrayMenu();
        _ = SaveWorkflowsQuietlyAsync();
    }

    private static async Task SaveWorkflowsQuietlyAsync()
    {
        try
        {
            // SettingsChanged would rebuild the tray and re-register the hotkeys; neither is needed here.
            await SettingsManager.SaveWorkflowsConfigAsync(raiseSettingsChanged: false);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Tray: unable to save the task settings");
        }
    }

    /// <summary>
    /// Runs a job with the default task settings, as ShareX's tray menu does. Captures and recordings run the way
    /// their hotkey would (a second click stops a recording); other jobs run directly, because the hotkey path
    /// switches the main window to the Editor page for them.
    /// </summary>
    private void RunJob(WorkflowType job, Action<TaskSettings>? configure = null)
    {
        TaskSettings taskSettings = TaskSettings.GetSafeTaskSettings(SettingsManager.DefaultTaskSettings);
        taskSettings.Job = job;
        configure?.Invoke(taskSettings);
        string category = job.GetHotkeyCategory();
        bool isCaptureJob = category == EnumExtensions.WorkflowType_Category_ScreenCapture ||
                            category == EnumExtensions.WorkflowType_Category_ScreenRecord;
        _ = RunAndRefreshAsync(() => isCaptureJob && _workflowOrchestrator != null
            ? _workflowOrchestrator.RunJobAsync(job, taskSettings)
            : Core.Helpers.TaskHelpers.ExecuteJob(job, taskSettings));
    }

    private void RunWorkflow(WorkflowSettings workflow)
    {
        _ = RunAndRefreshAsync(() => _workflowOrchestrator != null
            ? _workflowOrchestrator.RunWorkflowAsync(workflow)
            : Core.Helpers.TaskHelpers.ExecuteWorkflow(workflow, workflow.Id));
    }

    private async Task RunAndRefreshAsync(Func<Task> run)
    {
        try
        {
            await run();
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Tray: the menu command failed");
        }
        finally
        {
            // As ShareX refreshes its menus after a tray command (Enable/Disable hotkeys changes its own text).
            Avalonia.Threading.Dispatcher.UIThread.Post(BuildTrayMenu);
        }
    }

    private static Views.MainWindow? GetMainWindow() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as Views.MainWindow;

    private void NavigateMainWindow(string navTag)
    {
        if (GetMainWindow() is Views.MainWindow mainWindow)
        {
            mainWindow.ShowInTaskbar = ShouldShowInTaskbar();
            mainWindow.NavigateFromTray(navTag);
        }
    }

    private static void OpenTool(string navTag) => GetMainWindow()?.OpenTool(navTag);

    private static NativeMenuItem Item(string header, Action execute) => new(header) { Command = new RelayCommand(execute) };

    private static NativeMenuItem CheckItem(string header, bool isChecked, MenuItemToggleType toggleType, Action execute) =>
        new(header) { Command = new RelayCommand(execute), ToggleType = toggleType, IsChecked = isChecked };

    private static NativeMenuItem Parent(string header, NativeMenu menu) => new(header) { Menu = menu };
}
