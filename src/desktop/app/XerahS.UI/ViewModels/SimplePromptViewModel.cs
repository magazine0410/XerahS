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

namespace XerahS.UI.ViewModels;

/// <summary>Shared modal prompt for message / confirm / text / secret input.</summary>
public partial class SimplePromptViewModel : ViewModelBase
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private bool _showCancel;
    [ObservableProperty] private bool _showInput;
    [ObservableProperty] private bool _isPassword;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isWarning;
    [ObservableProperty] private string _primaryButtonText = "OK";
    [ObservableProperty] private string _cancelButtonText = "Cancel";

    [ObservableProperty] private bool _showSecondary;
    [ObservableProperty] private string _secondaryButtonText = "No";
    public bool SecondaryChosen { get; private set; }

    [RelayCommand]
    private void Secondary()
    {
        SecondaryChosen = true;
        CloseRequested?.Invoke(false);
    }

    public Action<bool>? CloseRequested { get; set; }

    /// <summary>When input mode, set on OK; otherwise unused.</summary>
    public string? AcceptedInput { get; private set; }

    public bool Confirmed { get; private set; }

    [RelayCommand]
    private void Primary()
    {
        Confirmed = true;
        if (ShowInput)
            AcceptedInput = InputText;
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        Confirmed = false;
        AcceptedInput = null;
        CloseRequested?.Invoke(false);
    }
}
