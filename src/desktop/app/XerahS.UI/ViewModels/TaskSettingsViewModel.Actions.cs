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

public partial class TaskSettingsViewModel
{
    private TaskSettings ActionsSource => SourceFor(_settings.UseDefaultActions);
    private List<ExternalProgram>? _actionsModel;
    private ObservableCollection<ExternalActionViewModel> _actions = [];
    public ObservableCollection<ExternalActionViewModel> Actions
    {
        get
        {
            var model = ActionsSource.ExternalPrograms ??= [];
            if (!ReferenceEquals(_actionsModel, model))
            {
                _actionsModel = model;
                _actions = new(model.Select(action => new ExternalActionViewModel(action)));
                SelectedAction = _actions.FirstOrDefault();
            }
            return _actions;
        }
    }

    [ObservableProperty] private ExternalActionViewModel? _selectedAction;
    public bool ActionsSettingsEnabled => IsDefaultTaskSettings || OverrideActions;
    public bool OverrideActions
    {
        get => !_settings.UseDefaultActions;
        set => SetOverride(_settings.UseDefaultActions, value, useDefault => _settings.UseDefaultActions = useDefault);
    }

    [RelayCommand]
    private void AddAction()
    {
        if (!ActionsSettingsEnabled) return;
        var item = new ExternalActionViewModel(new ExternalProgram { IsActive = true, Name = "New action" });
        Actions.Add(item);
        ActionsSource.ExternalPrograms.Add(item.Model);
        SelectedAction = item;
    }

    [RelayCommand]
    private void RemoveAction()
    {
        if (!ActionsSettingsEnabled || SelectedAction == null) return;
        ActionsSource.ExternalPrograms.Remove(SelectedAction.Model);
        Actions.Remove(SelectedAction);
        SelectedAction = Actions.FirstOrDefault();
    }

    [RelayCommand] private void MoveActionUp() => MoveAction(-1);
    [RelayCommand] private void MoveActionDown() => MoveAction(1);
    private void MoveAction(int delta)
    {
        if (!ActionsSettingsEnabled || SelectedAction == null) return;
        int index = Actions.IndexOf(SelectedAction), target = index + delta;
        if (target < 0 || target >= Actions.Count) return;
        Actions.Move(index, target);
        ActionsSource.ExternalPrograms = Actions.Select(item => item.Model).ToList();
        _actionsModel = ActionsSource.ExternalPrograms;
    }
}

public sealed class ExternalActionViewModel : ObservableObject
{
    public ExternalProgram Model { get; }
    public ExternalActionViewModel(ExternalProgram model) => Model = model;
    public bool IsActive
    {
        get => Model.IsActive;
        set { if (Model.IsActive != value) { Model.IsActive = value; OnPropertyChanged(); } }
    }
    public string? Name
    {
        get => Model.Name;
        set { if (Model.Name != value) { Model.Name = value; OnPropertyChanged(); } }
    }
    public string? Path
    {
        get => Model.Path;
        set { if (Model.Path != value) { Model.Path = value; OnPropertyChanged(); } }
    }
    public string? Args
    {
        get => Model.Args;
        set { if (Model.Args != value) { Model.Args = value; OnPropertyChanged(); } }
    }
    public string? OutputExtension
    {
        get => Model.OutputExtension;
        set { if (Model.OutputExtension != value) { Model.OutputExtension = value; OnPropertyChanged(); } }
    }
    public string? Extensions
    {
        get => Model.Extensions;
        set { if (Model.Extensions != value) { Model.Extensions = value; OnPropertyChanged(); } }
    }
    public bool HiddenWindow
    {
        get => Model.HiddenWindow;
        set { if (Model.HiddenWindow != value) { Model.HiddenWindow = value; OnPropertyChanged(); } }
    }
    public bool DeleteInputFile
    {
        get => Model.DeleteInputFile;
        set { if (Model.DeleteInputFile != value) { Model.DeleteInputFile = value; OnPropertyChanged(); } }
    }
}
