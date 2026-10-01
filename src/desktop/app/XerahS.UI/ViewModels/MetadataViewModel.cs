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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XerahS.Common;
using System.Text;
using XerahS.Media.Metadata;
using System.Text.RegularExpressions;

namespace XerahS.UI.ViewModels;

public sealed record MetadataEntry(string Group, string Tag, string Value, string? Url)
{
    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);
}

public sealed record MetadataGroup(string Name, IReadOnlyList<MetadataEntry> Entries);

public sealed partial class MetadataViewModel : ViewModelBase
{
    private readonly List<MetadataEntry> _allEntries = [];
    private readonly Action? _playNotificationSound;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName))]
    [NotifyPropertyChangedFor(nameof(FileDescription))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(CanStrip))]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<MetadataGroup> _groups = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    [NotifyPropertyChangedFor(nameof(CanCopy))]
    [NotifyPropertyChangedFor(nameof(CanStrip))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isConfirmingStrip;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _emptyMessage = "Open or drop a file to inspect its metadata";

    public Func<Task<string?>>? SelectFileRequested { get; set; }
    public Func<string, Task>? CopyTextRequested { get; set; }

    public string FileName => File.Exists(FilePath) ? Path.GetFileName(FilePath) : "No file selected";
    public string FileDescription => File.Exists(FilePath) ? FilePath : "Open or drop any supported file";
    public string WindowTitle => File.Exists(FilePath) ? string.Format("XerahS - Metadata - {0}", FileName) : "XerahS - Metadata";
    public bool HasGroups => Groups.Count > 0;
    public bool CanOpen => !IsBusy;
    public bool CanCopy => !IsBusy && _allEntries.Count > 0;
    public bool CanStrip => !IsBusy && MetadataService.CanStripMetadata(FilePath);
    public string MetadataCountText => _allEntries.Count == 1 ? "1 tag" : string.Format("{0:N0} tags", _allEntries.Count);

    public MetadataViewModel(string? filePath = null, Action? playNotificationSound = null)
    {
        _filePath = filePath ?? string.Empty;
        _playNotificationSound = playNotificationSound;
    }

    public async Task StartAsync()
    {
        if (File.Exists(FilePath))
        {
            await LoadMetadataAsync();
        }
        else
        {
            await BrowseAsync();
        }
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (!CanOpen || SelectFileRequested == null)
        {
            return;
        }

        try
        {
            string? filePath = await SelectFileRequested();
            if (!string.IsNullOrWhiteSpace(filePath)) await OpenFileAsync(filePath);
        }
        catch (Exception ex) { Services.UploadWorkflowService.ReportError(ex, "Unable to open file"); }
    }

    public async Task OpenFileAsync(string filePath)
    {
        if (IsBusy || !File.Exists(filePath))
        {
            return;
        }

        FilePath = filePath;
        SearchText = string.Empty;
        await LoadMetadataAsync();
    }

    private async Task LoadMetadataAsync()
    {
        IsBusy = true;
        IsConfirmingStrip = false;
        Groups = [];
        EmptyMessage = "Reading metadata...";
        _allEntries.Clear();
        NotifyMetadataState();

        try
        {
            IReadOnlyList<MetadataValue> values = await MetadataService.ReadMetadataAsync(FilePath);
            _allEntries.AddRange(values.Select(value =>
            {
                Match urlMatch = UrlRegex().Match(value.Value);
                return new MetadataEntry(value.Group, value.Tag, value.Value, urlMatch.Success ? urlMatch.Value : null);
            }));
            EmptyMessage = _allEntries.Count > 0
                ? "No metadata matches the current search"
                : "No metadata was found in this file";
            ApplyFilter();
        }
        catch (Exception ex)
        {
            EmptyMessage = string.Format("Unable to read metadata\n{0}", ex.Message);
            DebugHelper.WriteException(ex, "Failed to read file metadata.");
        }
        finally
        {
            IsBusy = false;
            NotifyMetadataState();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    private async Task CopyAllAsync()
    {
        if (CanCopy && CopyTextRequested != null)
        {
            try { await CopyTextRequested(BuildMetadataText(_allEntries)); }
            catch (Exception ex) { Services.UploadWorkflowService.ReportError(ex, "Unable to copy metadata"); }
        }
    }

    [RelayCommand]
    private void RequestStrip()
    {
        if (CanStrip)
        {
            IsConfirmingStrip = true;
        }
    }

    [RelayCommand]
    private void CancelStrip()
    {
        IsConfirmingStrip = false;
    }

    [RelayCommand]
    private async Task ConfirmStripAsync()
    {
        if (!CanStrip)
        {
            return;
        }

        IsConfirmingStrip = false;
        IsBusy = true;
        try
        {
            await MetadataService.StripMetadataAsync(FilePath);
            _playNotificationSound?.Invoke();
        }
        catch (Exception ex)
        {
            IsBusy = false;
            Groups = [];
            OnPropertyChanged(nameof(HasGroups));
            EmptyMessage = string.Format("Unable to strip metadata\n{0}", ex.Message);
            DebugHelper.WriteException(ex, "Failed to strip file metadata.");
            return;
        }

        IsBusy = false;
        await LoadMetadataAsync();
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            URLHelpers.OpenURL(url);
        }
    }

    private void ApplyFilter()
    {
        string filter = SearchText.Trim();
        IEnumerable<MetadataEntry> entries = _allEntries;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            entries = entries.Where(x =>
                x.Group.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                x.Tag.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                x.Value.Contains(filter, StringComparison.CurrentCultureIgnoreCase));
        }

        Groups = entries
            .GroupBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
            .Select(x => new MetadataGroup(x.Key, x.ToArray()))
            .ToArray();
        OnPropertyChanged(nameof(HasGroups));
    }

    private void NotifyMetadataState()
    {
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(CanStrip));
        OnPropertyChanged(nameof(MetadataCountText));
    }

    private static string BuildMetadataText(IEnumerable<MetadataEntry> entries)
    {
        StringBuilder output = new();
        foreach (IGrouping<string, MetadataEntry> group in entries.GroupBy(x => x.Group, StringComparer.OrdinalIgnoreCase))
        {
            if (output.Length > 0)
            {
                output.AppendLine();
            }
            output.AppendLine($"# {group.Key}");
            foreach (MetadataEntry entry in group)
            {
                output.AppendLine($"    {entry.Tag}: {entry.Value}");
            }
        }
        return output.ToString().TrimEnd();
    }

    [GeneratedRegex(@"https?://[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}
