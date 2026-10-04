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
using Avalonia.Input;
using Avalonia.Interactivity;
using XerahS.Core;
using XerahS.UI.ViewModels;

namespace XerahS.UI.Views;

public partial class QuickTaskMenuEditorWindow : SurfaceWindow
{
    public QuickTaskMenuEditorWindow()
    {
        InitializeComponent();
        DataContext = new QuickTaskMenuEditorViewModel(SettingsManager.Settings.QuickTaskPresets ?? []);
        RequestedThemeVariant = ShareX.ImageEditor.Presentation.Theming.ThemeManager.GetCurrentTheme();
    }
    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not QuickTaskMenuEditorViewModel viewModel) return;
        SettingsManager.Settings.QuickTaskPresets = viewModel.Presets.Select(preset => preset.Model).ToList();
        await SettingsManager.SaveApplicationConfigAsync();
        Close();
    }
    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }
}
