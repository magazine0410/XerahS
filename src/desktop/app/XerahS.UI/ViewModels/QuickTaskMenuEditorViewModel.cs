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
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XerahS.Common;
using XerahS.Core;

namespace XerahS.UI.ViewModels;

public partial class QuickTaskMenuEditorViewModel : ObservableObject
{
    public ObservableCollection<QuickTaskPresetViewModel> Presets { get; }
    [ObservableProperty] private QuickTaskPresetViewModel? _selectedPreset;

    public QuickTaskMenuEditorViewModel(IEnumerable<QuickTaskInfo> presets)
    {
        Presets = new(presets.Select(preset => new QuickTaskPresetViewModel(new QuickTaskInfo
        {
            Name = preset.Name, AfterCapture = preset.AfterCapture, AfterUpload = preset.AfterUpload
        })));
        SelectedPreset = Presets.FirstOrDefault();
    }

    [RelayCommand] private void Add() => AddPreset(new QuickTaskInfo { Name = "New task", AfterCapture = AfterCaptureTasks.SaveImageToFile });
    [RelayCommand] private void AddSeparator() => AddPreset(new QuickTaskInfo());
    private void AddPreset(QuickTaskInfo preset)
    {
        SelectedPreset = new QuickTaskPresetViewModel(preset);
        Presets.Add(SelectedPreset);
    }
    [RelayCommand] private void Remove()
    {
        if (SelectedPreset != null) Presets.Remove(SelectedPreset);
        SelectedPreset = Presets.FirstOrDefault();
    }
    [RelayCommand] private void MoveUp() => Move(-1);
    [RelayCommand] private void MoveDown() => Move(1);
    private void Move(int delta)
    {
        if (SelectedPreset == null) return;
        int from = Presets.IndexOf(SelectedPreset), to = from + delta;
        if (to >= 0 && to < Presets.Count) Presets.Move(from, to);
    }
}

public sealed class QuickTaskPresetViewModel : ObservableObject
{
    public QuickTaskInfo Model { get; }
    public string Name { get => Model.Name; set { Model.Name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } }
    public string DisplayName => Model.IsValid ? Model.ToString() : "────────";
    public IReadOnlyList<QuickTaskOption> CaptureOptions { get; }
    public IReadOnlyList<QuickTaskOption> UploadOptions { get; }

    public QuickTaskPresetViewModel(QuickTaskInfo model)
    {
        Model = model;
        CaptureOptions = Enum.GetValues<AfterCaptureTasks>().Distinct().Where(flag => flag != AfterCaptureTasks.None)
            .OrderBy(flag => flag == AfterCaptureTasks.CopyFolderPathToClipboard ? (1 << 13) + 1 : (int)flag)
            .Select(flag => new QuickTaskOption(flag == AfterCaptureTasks.AnnotateMedia ? "Annotate media" : flag.GetLocalizedDescription(), model.AfterCapture.HasFlag(flag), selected =>
            {
                model.AfterCapture = selected ? model.AfterCapture | flag : model.AfterCapture & ~flag;
                OnPropertyChanged(nameof(DisplayName));
            })).ToList();
        UploadOptions = Enum.GetValues<AfterUploadTasks>().Where(flag => flag != AfterUploadTasks.None)
            .Select(flag => new QuickTaskOption(flag switch
            {
                AfterUploadTasks.ShowAfterUploadWindow => "Show after-upload window",
                AfterUploadTasks.UseURLShortener => "Shorten URL",
                AfterUploadTasks.ShareURL => "Share URL",
                AfterUploadTasks.CopyURLToClipboard => "Copy URL to clipboard",
                AfterUploadTasks.OpenURL => "Open URL",
                AfterUploadTasks.ShowQRCode => "Show QR code",
                _ => flag.GetLocalizedDescription()
            }, model.AfterUpload.HasFlag(flag), selected =>
                model.AfterUpload = selected ? model.AfterUpload | flag : model.AfterUpload & ~flag)).ToList();
    }
}

public sealed class QuickTaskOption : ObservableObject
{
    private bool _selected;
    private readonly Action<bool> _changed;
    public string Name { get; }
    public bool Selected { get => _selected; set { if (SetProperty(ref _selected, value)) _changed(value); } }
    public QuickTaskOption(string name, bool selected, Action<bool> changed)
    {
        Name = name; _selected = selected; _changed = changed;
    }
}
