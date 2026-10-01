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

using XerahS.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using XerahS.Common;
using XerahS.Common.Converters;
using XerahS.Bootstrap;
using XerahS.Core;
using XerahS.Indexer;
using XerahS.Core.Managers;
using XerahS.Platform.Abstractions;

namespace XerahS.UI.ViewModels;

public partial class IndexFolderViewModel : ViewModelBase
{
    private readonly TaskSettings _taskSettings;
    private readonly bool _isWorkflowConfigMode;
    private readonly string _tempHtmlPath;
    private CancellationTokenSource? _indexingCancellationTokenSource;
    private readonly Progress<XerahS.Indexer.IndexerProgress> _indexerProgress;
    private readonly IViewDialogService _dialogService;
    private readonly IDesktopTaskManager _taskManager;

    [ObservableProperty]
    private string _folderPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyOutputCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenderHtmlPreviewCommand))]
    private string _outputText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Select a folder to index.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAsCommand))]
    [NotifyCanExecuteChangedFor(nameof(IndexFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelIndexingCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadCommand))]
    private string _generatedFilePath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenderHtmlPreviewCommand))]
    private bool _isHtmlOutput;

    [ObservableProperty]
    private string _folderPathError = string.Empty;

    /// <summary>
    /// The Index Folder tool uses the settings the task runs with (the default tool settings unless the
    /// task overrides them). The workflow editor edits the task's own settings when it overrides them,
    /// and shows the defaults, disabled, when it does not; it checks the edited copy's flag.
    /// </summary>
    private TaskSettingsTools Tools => !_isWorkflowConfigMode
        ? _taskSettings.ToolsSettingsReference
        : _taskSettings.UseDefaultToolsSettings && SettingsManager.DefaultTaskSettings != null
            ? SettingsManager.DefaultTaskSettings.ToolsSettings ??= new TaskSettingsTools()
            : _taskSettings.ToolsSettings ??= new TaskSettingsTools();

    /// <summary>
    /// Reloads the controls after the workflow editor's "Override tools settings" changes.
    /// </summary>
    public void ReloadToolsSettings()
    {
        FolderPath = Tools.IndexerFolderPath;
        IsHtmlOutput = Tools.IndexerSettings.Output == IndexerOutput.Html;
        OnPropertyChanged(string.Empty);
    }

    public IndexFolderViewModel(TaskSettings? taskSettings, bool isWorkflowConfigMode, IViewDialogService dialogService, IDesktopTaskManager taskManager)
    {
        _dialogService = dialogService;
        _taskManager = taskManager;
        var workflow = SettingsManager.GetFirstWorkflow(WorkflowType.IndexFolder);
        _taskSettings = taskSettings ?? workflow?.TaskSettings ?? new TaskSettings { Job = WorkflowType.IndexFolder };
        _isWorkflowConfigMode = isWorkflowConfigMode;
        _tempHtmlPath = Path.Combine(Path.GetTempPath(), $"xerahs_index_{Guid.NewGuid():N}.html");

        FolderPath = Tools.IndexerFolderPath;
        IsHtmlOutput = Tools.IndexerSettings.Output == IndexerOutput.Html;
        
        // Initialize progress reporter for async indexing
        _indexerProgress = new Progress<XerahS.Indexer.IndexerProgress>(OnIndexingProgress);
    }
    
    private void OnIndexingProgress(XerahS.Indexer.IndexerProgress progress)
    {
        if (progress.FilesProcessed > 0 || progress.FoldersProcessed > 0)
        {
            StatusMessage = $"Indexing... {progress.FilesProcessed} files, {progress.FoldersProcessed} folders";
            if (!string.IsNullOrEmpty(progress.CurrentItem))
            {
                StatusMessage += $" - {Path.GetFileName(progress.CurrentItem)}";
            }
        }
    }

    partial void OnFolderPathChanged(string value)
    {
        Tools.IndexerFolderPath = value;
        if (!string.IsNullOrWhiteSpace(FolderPathError))
        {
            FolderPathError = string.Empty;
        }
    }

    public IEnumerable<IndexerOutput> IndexerOutputs => Enum.GetValues(typeof(IndexerOutput)).Cast<IndexerOutput>();

    public IndexerOutput Output
    {
        get => Tools.IndexerSettings.Output;
        set
        {
            if (Tools.IndexerSettings.Output != value)
            {
                Tools.IndexerSettings.Output = value;
                OnPropertyChanged();
                OnOutputChanged(value);
            }
        }
    }

    public bool SkipHiddenFolders
    {
        get => Tools.IndexerSettings.SkipHiddenFolders;
        set
        {
            if (Tools.IndexerSettings.SkipHiddenFolders != value)
            {
                Tools.IndexerSettings.SkipHiddenFolders = value;
                OnPropertyChanged();
            }
        }
    }

    public bool SkipHiddenFiles
    {
        get => Tools.IndexerSettings.SkipHiddenFiles;
        set
        {
            if (Tools.IndexerSettings.SkipHiddenFiles != value)
            {
                Tools.IndexerSettings.SkipHiddenFiles = value;
                OnPropertyChanged();
            }
        }
    }

    public bool SkipFiles
    {
        get => Tools.IndexerSettings.SkipFiles;
        set
        {
            if (Tools.IndexerSettings.SkipFiles != value)
            {
                Tools.IndexerSettings.SkipFiles = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IgnoreEmptyFolders
    {
        get => Tools.IndexerSettings.IgnoreEmptyFolders;
        set
        {
            if (Tools.IndexerSettings.IgnoreEmptyFolders != value)
            {
                Tools.IndexerSettings.IgnoreEmptyFolders = value;
                OnPropertyChanged();
            }
        }
    }

    public int MaxDepthLevel
    {
        get => Tools.IndexerSettings.MaxDepthLevel;
        set
        {
            if (Tools.IndexerSettings.MaxDepthLevel != value)
            {
                Tools.IndexerSettings.MaxDepthLevel = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowSizeInfo
    {
        get => Tools.IndexerSettings.ShowSizeInfo;
        set
        {
            if (Tools.IndexerSettings.ShowSizeInfo != value)
            {
                Tools.IndexerSettings.ShowSizeInfo = value;
                OnPropertyChanged();
            }
        }
    }

    public bool AddFooter
    {
        get => Tools.IndexerSettings.AddFooter;
        set
        {
            if (Tools.IndexerSettings.AddFooter != value)
            {
                Tools.IndexerSettings.AddFooter = value;
                OnPropertyChanged();
            }
        }
    }

    public string IndentationText
    {
        get => Tools.IndexerSettings.IndentationText;
        set
        {
            if (Tools.IndexerSettings.IndentationText != value)
            {
                Tools.IndexerSettings.IndentationText = value;
                OnPropertyChanged();
            }
        }
    }

    public bool AddEmptyLineAfterFolders
    {
        get => Tools.IndexerSettings.AddEmptyLineAfterFolders;
        set
        {
            if (Tools.IndexerSettings.AddEmptyLineAfterFolders != value)
            {
                Tools.IndexerSettings.AddEmptyLineAfterFolders = value;
                OnPropertyChanged();
            }
        }
    }

    public bool UseCustomCssFile
    {
        get => Tools.IndexerSettings.UseCustomCSSFile;
        set
        {
            if (Tools.IndexerSettings.UseCustomCSSFile != value)
            {
                Tools.IndexerSettings.UseCustomCSSFile = value;
                OnPropertyChanged();
            }
        }
    }

    public bool DisplayPath
    {
        get => Tools.IndexerSettings.DisplayPath;
        set
        {
            if (Tools.IndexerSettings.DisplayPath != value)
            {
                Tools.IndexerSettings.DisplayPath = value;
                OnPropertyChanged();
            }
        }
    }

    public bool DisplayPathLimited
    {
        get => Tools.IndexerSettings.DisplayPathLimited;
        set
        {
            if (Tools.IndexerSettings.DisplayPathLimited != value)
            {
                Tools.IndexerSettings.DisplayPathLimited = value;
                OnPropertyChanged();
            }
        }
    }

    public string CustomCssFilePath
    {
        get => Tools.IndexerSettings.CustomCSSFilePath;
        set
        {
            if (Tools.IndexerSettings.CustomCSSFilePath != value)
            {
                Tools.IndexerSettings.CustomCSSFilePath = value;
                OnPropertyChanged();
            }
        }
    }

    public bool UseAttribute
    {
        get => Tools.IndexerSettings.UseAttribute;
        set
        {
            if (Tools.IndexerSettings.UseAttribute != value)
            {
                Tools.IndexerSettings.UseAttribute = value;
                OnPropertyChanged();
            }
        }
    }

    public bool CreateParseableJson
    {
        get => Tools.IndexerSettings.CreateParseableJson;
        set
        {
            if (Tools.IndexerSettings.CreateParseableJson != value)
            {
                Tools.IndexerSettings.CreateParseableJson = value;
                OnPropertyChanged();
            }
        }
    }

    public string IncludedFileExtensionsText
    {
        get => Tools.IndexerSettings.IncludedFileExtensions != null 
            ? string.Join(", ", Tools.IndexerSettings.IncludedFileExtensions) 
            : string.Empty;
        set
        {
            var list = ParseExtensionsText(value);
            if (!ListEquals(Tools.IndexerSettings.IncludedFileExtensions, list))
            {
                Tools.IndexerSettings.IncludedFileExtensions = list;
                OnPropertyChanged();
            }
        }
    }

    public string ExcludedFileExtensionsText
    {
        get => Tools.IndexerSettings.ExcludedFileExtensions != null 
            ? string.Join(", ", Tools.IndexerSettings.ExcludedFileExtensions) 
            : string.Empty;
        set
        {
            var list = ParseExtensionsText(value);
            if (!ListEquals(Tools.IndexerSettings.ExcludedFileExtensions, list))
            {
                Tools.IndexerSettings.ExcludedFileExtensions = list;
                OnPropertyChanged();
            }
        }
    }

    private static List<string>? ParseExtensionsText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        
        var extensions = text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(ext => ext.Trim().ToLowerInvariant())
            .Where(ext => !string.IsNullOrEmpty(ext))
            .Select(ext => ext.StartsWith(".") ? ext.TrimStart('.') : ext)
            .Distinct()
            .ToList();
        
        return extensions.Count > 0 ? extensions : null;
    }

    private static bool ListEquals(List<string>? a, List<string>? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.Count != b.Count) return false;
        return a.SequenceEqual(b);
    }

    public bool BinaryUnits
    {
        get => Tools.IndexerSettings.BinaryUnits;
        set
        {
            if (Tools.IndexerSettings.BinaryUnits != value)
            {
                Tools.IndexerSettings.BinaryUnits = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HasOutput => !string.IsNullOrWhiteSpace(OutputText);

    public bool CanUpload => !string.IsNullOrWhiteSpace(GeneratedFilePath) && File.Exists(GeneratedFilePath) && !IsBusy;

    public bool CanSave => (HasOutput || (!string.IsNullOrWhiteSpace(GeneratedFilePath) && File.Exists(GeneratedFilePath))) && !IsBusy;

    public bool CanRenderHtml => IsHtmlOutput && (HasOutput || (!string.IsNullOrWhiteSpace(GeneratedFilePath) && File.Exists(GeneratedFilePath)));
    
    public bool CanStartIndexing => !IsBusy;
    
    public bool CanCancelIndexing => IsBusy && _indexingCancellationTokenSource != null;

    public bool IsNotBusy => !IsBusy;

    public bool IsWorkflowConfigMode => _isWorkflowConfigMode;

    public bool ShowUpload => !IsWorkflowConfigMode;

    public bool ShowSave => !IsWorkflowConfigMode;

    public string PrimaryActionLabel => IsWorkflowConfigMode ? "Test Index" : "Index";

    /// <summary>
    /// Opens the HTML output in the system default browser.
    /// </summary>

    public bool HasFolderPathError => !string.IsNullOrWhiteSpace(FolderPathError);

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        var folderPath = await _dialogService.ShowFolderPickerAsync("Select Folder to Index");
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            FolderPath = folderPath;
        }
    }

    [RelayCommand]
    private async Task BrowseCssAsync()
    {
        var filePath = await _dialogService.ShowFilePickerAsync("Select Custom CSS File", new[] { "*.css", "*.*" });
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            CustomCssFilePath = filePath;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartIndexing))]
    private async Task IndexFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
        {
            StatusMessage = "Select a valid folder to index.";
            FolderPathError = "Folder does not exist.";
            return;
        }

        FolderPathError = string.Empty;
        IsBusy = true;
        _indexingCancellationTokenSource = new CancellationTokenSource();
        StatusMessage = "Indexing folder...";
        OutputText = string.Empty;
        GeneratedFilePath = string.Empty;
        OnPropertyChanged(nameof(HasOutput));
        OnPropertyChanged(nameof(CanUpload));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanRenderHtml));
        OnPropertyChanged(nameof(CanStartIndexing));
        OnPropertyChanged(nameof(CanCancelIndexing));

        try
        {
            _taskSettings.Job = WorkflowType.IndexFolder;
            Tools.IndexerFolderPath = FolderPath;

            var indexerSettings = Tools.IndexerSettings ?? new XerahS.Indexer.IndexerSettings();
            
            // Use async indexer with progress reporting and cancellation support
            string outputExtension = GetOutputExtension(Output);
            string outputPath = Path.Combine(Path.GetTempPath(), $"xerahs_index_{Guid.NewGuid():N}.{outputExtension}");
            var result = await XerahS.Indexer.IndexerAsync.IndexWithPreviewAsync(
                FolderPath, 
                outputPath,
                indexerSettings,
                maxPreviewLines: 10000,
                _indexerProgress,
                _indexingCancellationTokenSource.Token);

            if (!result.Result.Success)
            {
                throw new InvalidOperationException(result.Result.ErrorMessage ?? "Indexer returned an unknown error.");
            }

            if (string.IsNullOrWhiteSpace(result.Result.OutputFilePath) || !File.Exists(result.Result.OutputFilePath))
            {
                throw new IOException("Indexer completed without producing an output file.");
            }

            GeneratedFilePath = WriteIndexOutput(_taskSettings, result.Result.OutputFilePath, result.Preview);
            OutputText = result.Preview;
            StatusMessage = $"Index generated: {GeneratedFilePath}";
            SaveWorkflowSettingsIfAvailable();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Indexing cancelled.";
            OutputText = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Indexing failed: {ex.Message}";
            DebugHelper.WriteException(ex, "IndexFolder");
        }
        finally
        {
            IsBusy = false;
            _indexingCancellationTokenSource?.Dispose();
            _indexingCancellationTokenSource = null;
            OnPropertyChanged(nameof(HasOutput));
            OnPropertyChanged(nameof(CanUpload));
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(CanRenderHtml));
            OnPropertyChanged(nameof(CanStartIndexing));
            OnPropertyChanged(nameof(CanCancelIndexing));
        }
    }
    
    [RelayCommand(CanExecute = nameof(CanCancelIndexing))]
    private void CancelIndexing()
    {
        _indexingCancellationTokenSource?.Cancel();
        StatusMessage = "Cancelling...";
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        OnPropertyChanged(nameof(CanUpload));
        OnPropertyChanged(nameof(CanSave));
    }

    partial void OnOutputTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasOutput));
        OnPropertyChanged(nameof(CanUpload));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanRenderHtml));
    }

    partial void OnGeneratedFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(CanUpload));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanRenderHtml));
    }

    private void OnOutputChanged(IndexerOutput value)
    {
        IsHtmlOutput = value == IndexerOutput.Html;
    }


    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadAsync()
    {
        if (!CanUpload)
        {
            return;
        }

        var settings = GetUploadTaskSettings();
        settings.Job = WorkflowType.FileUpload;

        await _taskManager.StartFileTask(settings, GeneratedFilePath);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsAsync()
    {
        if (!CanSave)
        {
            return;
        }

        string suggestedName = GetSuggestedFileName();
        string extension = GetOutputExtension(Output);
        
        var filePath = await _dialogService.ShowSaveFilePickerAsync("Save Index Output", suggestedName, extension, new[] { $"*.{extension}", "*.*" });

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(GeneratedFilePath) && File.Exists(GeneratedFilePath))
        {
            File.Copy(GeneratedFilePath, filePath, overwrite: true);
        }
        else
        {
            await File.WriteAllTextAsync(filePath, OutputText);
        }

        StatusMessage = $"Saved to {filePath}";
    }

    [RelayCommand(CanExecute = nameof(HasOutput))]
    private async Task CopyOutputAsync()
    {
        if (!HasOutput || !PlatformServices.IsInitialized)
        {
            return;
        }

        await PlatformServices.Clipboard.SetTextAsync(OutputText);
        StatusMessage = "Output copied to clipboard.";
    }

    [RelayCommand(CanExecute = nameof(CanRenderHtml))]
    private void RenderHtmlPreview()
    {
        bool hasGeneratedHtml = !string.IsNullOrWhiteSpace(GeneratedFilePath) && File.Exists(GeneratedFilePath);
        if (!CanRenderHtml || (!hasGeneratedHtml && string.IsNullOrWhiteSpace(OutputText)))
        {
            return;
        }

        try
        {
            string htmlPath = !string.IsNullOrWhiteSpace(GeneratedFilePath) && File.Exists(GeneratedFilePath)
                ? GeneratedFilePath
                : _tempHtmlPath;

            if (string.Equals(htmlPath, _tempHtmlPath, StringComparison.Ordinal))
            {
                // Fallback for non-generated output states.
                File.WriteAllText(_tempHtmlPath, OutputText);
            }

            OpenInSystemBrowser(htmlPath);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "IndexFolder: Failed to open HTML preview");
        }
    }

    private static void OpenInSystemBrowser(string filePath)
    {
        string url = filePath;
        if (!url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            url = "file://" + url.Replace('\\', '/');
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", url);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", url);
        }
    }



    private string WriteIndexOutput(TaskSettings taskSettings, string sourceOutputFilePath, string fallbackOutput)
    {
        string extension = GetOutputExtension(Output);
        string screenshotsFolder = TaskHelpers.GetScreenshotsFolder(taskSettings);
        Directory.CreateDirectory(screenshotsFolder);

        string fileName = TaskHelpers.GetFileName(taskSettings, extension);
        string resolvedPath = TaskHelpers.HandleExistsFile(screenshotsFolder, fileName, taskSettings);
        if (string.IsNullOrWhiteSpace(resolvedPath)) return string.Empty;

        if (!string.IsNullOrWhiteSpace(sourceOutputFilePath) && File.Exists(sourceOutputFilePath))
        {
            File.Copy(sourceOutputFilePath, resolvedPath, overwrite: true);
        }
        else
        {
            File.WriteAllText(resolvedPath, fallbackOutput);
        }

        return resolvedPath;
    }

    private string GetOutputExtension(IndexerOutput output)
    {
        return output switch
        {
            IndexerOutput.Html => "html",
            IndexerOutput.Txt => "txt",
            IndexerOutput.Xml => "xml",
            IndexerOutput.Json => "json",
            _ => "txt"
        };
    }

    private string GetSuggestedFileName()
    {
        string extension = GetOutputExtension(Output);
        string baseName = string.IsNullOrWhiteSpace(FolderPath)
            ? "index"
            : Path.GetFileName(FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.IsNullOrEmpty(baseName))
        {
            baseName = "index";
        }

        return $"{baseName}.{extension}";
    }

    private static TaskSettings CloneTaskSettings(TaskSettings source)
    {
        var jsonSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Converters = new List<JsonConverter>
            {
                new StringEnumConverter(),
                new SkColorJsonConverter()
            }
        };

        string json = JsonConvert.SerializeObject(source, jsonSettings);
        return JsonConvert.DeserializeObject<TaskSettings>(json, jsonSettings) ?? new TaskSettings();
    }

    private static void SaveWorkflowSettingsIfAvailable()
    {
        if (SettingsManager.GetFirstWorkflow(WorkflowType.IndexFolder) != null)
        {
            SettingsManager.SaveWorkflowsConfig();
        }
    }



    private TaskSettings GetUploadTaskSettings()
    {
        var uploadWorkflow = SettingsManager.GetFirstWorkflow(WorkflowType.FileUpload);
        if (uploadWorkflow?.TaskSettings != null)
        {
            var settings = TaskSettings.GetSafeTaskSettings(uploadWorkflow.TaskSettings);
            settings.WorkflowId = uploadWorkflow.Id;
            return settings;
        }

        return CloneTaskSettings(_taskSettings);
    }
}
