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
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Hotkeys;
using XerahS.Tests.Xip0052;
using XerahS.UI.Services;
using XerahS.UI.ViewModels;
using XerahS.UI.Views;
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;
using EditorViewModel = ShareX.ImageEditor.Presentation.ViewModels.MainViewModel;

namespace XerahS.Tests.Views;

[TestFixture, NonParallelizable]
public class ApplicationIntegrationViewsTests
{
    [AvaloniaTest]
    public void SettingsPages_RenderAndPersistMaintenanceControls()
    {
        string previousFolder = SettingsManager.PersonalFolder;
        var previousSettings = SettingsManager.Settings;
        var previousWorkflows = SettingsManager.WorkflowsConfig;
        var previousUploaders = SettingsManager.UploadersConfig;
        string previousBrowser = HelpersOptions.BrowserPath;
        var previousFactory = UiViewModelFactoryAccessor.IsAvailable ? UiViewModelFactoryAccessor.GetRequired() : null;
        string root = Path.Combine(Path.GetTempPath(), "xerahs-settings-view-" + Guid.NewGuid().ToString("N"));
        Window? window = null;
        try
        {
            SettingsManager.PersonalFolder = root;
            typeof(SettingsManager).GetProperty(nameof(SettingsManager.Settings))!.SetValue(null, new ApplicationConfig());
            SettingsManager.WorkflowsConfig = new WorkflowsConfig();
            SettingsManager.UploadersConfig = new UploadersConfig();
            UiViewModelFactoryAccessor.Configure(new FakeUiViewModelFactory());
            var view = new ApplicationSettingsView();
            var vm = (SettingsViewModel)view.DataContext!;
            window = new Window { Content = view, Width = 1120, Height = 800 };
            window.Styles.Add(new global::Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://XerahS.UI/"))
            {
                Source = new Uri("avares://XerahS.UI/Themes/ThemeResources.axaml")
            });
            window.Show();
            Assert.That(view.SelectTabByHeader("Export / Import"), Is.True);
            window.UpdateLayout();
            var cleanup = view.GetVisualDescendants().OfType<CheckBox>()
                .Single(c => c.Content?.ToString() == "Automatically cleanup old backup files");
            cleanup.IsChecked = true;
            var keep = view.GetVisualDescendants().OfType<NumericUpDown>().Single();
            keep.Value = 7;
            Assert.Multiple(() =>
            {
                Assert.That(vm.AutoCleanupBackupFiles, Is.True);
                Assert.That(SettingsManager.Settings.CleanupKeepFileCount, Is.EqualTo(7));
                Assert.That(File.ReadAllText(SettingsManager.ApplicationConfigFilePath), Does.Contain("\"CleanupKeepFileCount\": 7"));
            });
            SavePreview(window, "settings-export-import");
            foreach (string tab in new[] { "Paths", "Integration", "Advanced" })
            {
                Assert.That(view.SelectTabByHeader(tab), Is.True);
                window.UpdateLayout();
                if (tab == "Advanced")
                {
                    var browser = view.GetVisualDescendants().OfType<TextBox>()
                        .Single(c => c.PlaceholderText == "Leave empty to use the default browser");
                    browser.BringIntoView();
                    window.UpdateLayout();
                }
                SavePreview(window, "settings-" + tab.ToLowerInvariant());
            }
        }
        finally
        {
            window?.Close();
            SettingsManager.PersonalFolder = previousFolder;
            typeof(SettingsManager).GetProperty(nameof(SettingsManager.Settings))!.SetValue(null, previousSettings);
            SettingsManager.WorkflowsConfig = previousWorkflows;
            SettingsManager.UploadersConfig = previousUploaders;
            HelpersOptions.BrowserPath = previousBrowser;
            if (previousFactory == null) UiViewModelFactoryAccessor.Reset();
            else UiViewModelFactoryAccessor.Configure(previousFactory);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [AvaloniaTest]
    public async Task ImportPrompt_DistinguishesYesNoCancelAndDismiss()
    {
        var previous = EditorViewModel.Current;
        var host = new EditorViewModel();
        try
        {
            for (int choice = 0; choice < 4; choice++)
            {
                Task<bool?> answer = AvaloniaDialogServiceAdapter.ShowYesNoCancelAsync("Custom uploader", "Add and activate?");
                Dispatcher.UIThread.RunJobs();
                var prompt = host.ModalContent as SimplePromptViewModel;
                Assert.That(prompt, Is.Not.Null);
                if (choice == 0) prompt!.PrimaryCommand.Execute(null);
                else if (choice == 1) prompt!.SecondaryCommand.Execute(null);
                else if (choice == 2) prompt!.CancelCommand.Execute(null);
                else host.CloseModalCommand.Execute(null);
                Assert.That(await answer, Is.EqualTo(choice == 0 ? true : choice == 1 ? false : (bool?)null));
                Assert.That(host.IsModalOpen, Is.False);
            }
        }
        finally
        {
            typeof(EditorViewModel).GetProperty(nameof(EditorViewModel.Current))!.SetValue(null, previous);
        }
    }

    [AvaloniaTest]
    public async Task CustomUploaderImport_YesActivates_NoPreservesDefault_AndCancelDoesNotImport()
    {
        string previousFolder = SettingsManager.PersonalFolder;
        var previousSettings = SettingsManager.Settings;
        var previousWorkflows = SettingsManager.WorkflowsConfig;
        var previousUploaders = SettingsManager.UploadersConfig;
        string previousBrowser = HelpersOptions.BrowserPath;
        var previousHost = EditorViewModel.Current;
        var host = new EditorViewModel();
        string root = Path.Combine(Path.GetTempPath(), "xerahs-uploader-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configurationField = typeof(InstanceManager).GetField("_configuration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        object? previousInstances = null;
        try
        {
            SettingsManager.PersonalFolder = root;
            typeof(SettingsManager).GetProperty(nameof(SettingsManager.Settings))!.SetValue(null, new ApplicationConfig());
            SettingsManager.WorkflowsConfig = new WorkflowsConfig();
            SettingsManager.UploadersConfig = new UploadersConfig();
            previousInstances = configurationField.GetValue(InstanceManager.Instance);
            configurationField.SetValue(InstanceManager.Instance, new InstanceConfiguration());
            var vm = new DestinationSettingsViewModel(new FakeUiViewModelFactory());
            string? activatedId = null;
            for (int choice = 0; choice < 3; choice++)
            {
                string source = Path.Combine(root, $"source{choice}.sxcu");
                var item = CustomUploaderItem.Init();
                item.Name = "Imported " + choice;
                item.RequestURL = "https://example.test/upload";
                item.DestinationType = CustomUploaderDestinationType.ImageUploader;
                await File.WriteAllTextAsync(source, Newtonsoft.Json.JsonConvert.SerializeObject(item));
                void AnswerPrompt(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
                {
                    if (args.PropertyName != nameof(host.IsModalOpen) || !host.IsModalOpen || host.ModalContent is not SimplePromptViewModel prompt) return;
                    if (choice == 0) prompt.PrimaryCommand.Execute(null);
                    else if (choice == 1) prompt.SecondaryCommand.Execute(null);
                    else prompt.CancelCommand.Execute(null);
                }
                host.PropertyChanged += AnswerPrompt;
                try { await vm.ImportCustomUploaderFileAsync(source); }
                finally { host.PropertyChanged -= AnswerPrompt; }
                var instances = InstanceManager.Instance.GetInstancesByCategory(UploaderCategory.Image);
                Assert.That(instances.Count, Is.EqualTo(choice == 0 ? 1 : 2));
                if (choice == 0) activatedId = instances.Single().InstanceId;
                Assert.That(InstanceManager.Instance.GetDefaultInstance(UploaderCategory.Image)?.InstanceId, Is.EqualTo(activatedId));
                Assert.That(File.Exists(Path.Combine(PathsManager.PluginsFolder, $"Imported_{choice}.sxcu")), Is.EqualTo(choice != 2));
            }
            string invalid = Path.Combine(root, "invalid.sxcu");
            await File.WriteAllTextAsync(invalid, "{}");
            try
            {
                await vm.ImportCustomUploaderFileAsync(invalid);
                Assert.Fail("Invalid definitions must be rejected before importing.");
            }
            catch (InvalidDataException) { }
            Assert.That(Directory.GetFiles(PathsManager.PluginsFolder, "*.sxcu").Length, Is.EqualTo(2));
        }
        finally
        {
            foreach (var provider in ProviderCatalog.GetCustomUploaderProviders().Where(p => p.FilePath.StartsWith(root, StringComparison.Ordinal)).ToList())
                ProviderCatalog.RemoveCustomUploader(provider.ProviderId);
            if (previousInstances != null) configurationField.SetValue(InstanceManager.Instance, previousInstances);
            SettingsManager.PersonalFolder = previousFolder;
            typeof(SettingsManager).GetProperty(nameof(SettingsManager.Settings))!.SetValue(null, previousSettings);
            SettingsManager.WorkflowsConfig = previousWorkflows;
            SettingsManager.UploadersConfig = previousUploaders;
            HelpersOptions.BrowserPath = previousBrowser;
            typeof(EditorViewModel).GetProperty(nameof(EditorViewModel.Current))!.SetValue(null, previousHost);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void SavePreview(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null);
        string? directory = Environment.GetEnvironmentVariable("XERAHS_UI_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        frame.Save(stream, PngBitmapEncoderOptions.Default);
    }
}
