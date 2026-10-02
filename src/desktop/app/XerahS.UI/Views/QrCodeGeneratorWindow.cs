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
using Avalonia.Input;
using XerahS.UI.ViewModels;

namespace XerahS.UI.Views;

/// <summary>A standalone QR result window that can open while the main window is in the tray.</summary>
public sealed class QrCodeGeneratorWindow : SurfaceWindow
{
    public QrCodeGeneratorWindow(QrCodeGeneratorViewModel viewModel, string text)
    {
        Title = "QR code";
        Width = 1000;
        Height = 660;
        MinWidth = 820;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RequestedThemeVariant = ShareX.ImageEditor.Presentation.Theming.ThemeManager.GetCurrentTheme();
        DataContext = viewModel;
        Content = new QrCodeGeneratorDialog { DataContext = viewModel };
        viewModel.CloseRequested = _ => Close();
        Closed += (_, _) => viewModel.Dispose();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        viewModel.InputText = text;
        viewModel.GenerateCommand.Execute(null);
    }
}
