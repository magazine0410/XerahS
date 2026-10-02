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
using XerahS.Common;

namespace XerahS.UI.ViewModels;

/// <summary>ShareX's print options window. As in ShareX, changes go straight into the saved print settings.</summary>
public sealed partial class PrintOptionsViewModel : ViewModelBase
{
    private readonly PrintSettings _settings;

    public PrintOptionsViewModel(PrintSettings settings, bool previewOnly)
    {
        _settings = settings;
        CanPrint = !previewOnly;
    }

    public bool CanPrint { get; }

    [ObservableProperty] private bool _isBusy;

    public string PrintButtonText => "Print" + (_settings.ShowPrintDialog ? "..." : string.Empty);

    public decimal Margin
    {
        get => _settings.Margin;
        set { _settings.Margin = (int)Math.Clamp(value, 0, 1000); OnPropertyChanged(); }
    }

    public bool AutoRotateImage
    {
        get => _settings.AutoRotateImage;
        set { _settings.AutoRotateImage = value; OnPropertyChanged(); }
    }

    public bool AutoScaleImage
    {
        get => _settings.AutoScaleImage;
        set { _settings.AutoScaleImage = value; OnPropertyChanged(); }
    }

    public bool AllowEnlargeImage
    {
        get => _settings.AllowEnlargeImage;
        set { _settings.AllowEnlargeImage = value; OnPropertyChanged(); }
    }

    public bool CenterImage
    {
        get => _settings.CenterImage;
        set { _settings.CenterImage = value; OnPropertyChanged(); }
    }
}
