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
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using ShareX.ImageEditor.Presentation.ViewModels;
using ShareX.ImageEditor.Presentation.Views;
using System;
using XerahS.Common;
using XerahS.UI.Controls;
using XerahS.UI.Helpers;
using XerahS.UI.ViewModels;

namespace XerahS.UI.Views
{
    public partial class TaskSettingsPanel : UserControl
    {
        public TaskSettingsPanel()
        {
            InitializeComponent();

            TextBox? namePattern = this.FindControl<TextBox>("NameFormatPatternTextBox");
            if (namePattern != null)
            {
                NamePatternMenu.Attach(namePattern, CodeMenuEntryFilename.n, CodeMenuEntryFilename.t, CodeMenuEntryFilename.pn);
            }

            TextBox? windowPattern = this.FindControl<TextBox>("NameFormatPatternActiveWindowTextBox");
            if (windowPattern != null)
            {
                NamePatternMenu.Attach(windowPattern, CodeMenuEntryFilename.n);
            }

            // Wire up PropertyGrid property changes to preview updates
            var propertyGrid = this.FindControl<PropertyGrid>("EffectPropertyGrid");
            if (propertyGrid != null)
            {
                propertyGrid.PropertyValueChanged += (s, e) =>
                {
                    if (DataContext is TaskSettingsViewModel vm)
                    {
                        vm.ImageEffects.UpdatePreview();
                    }
                };
            }
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is TaskSettingsViewModel vm)
            {
                Console.WriteLine($"[TaskSettingsPanel] DataContext set. Preset='{vm.ImageEffects.Name}', Effects={vm.ImageEffects.Effects.Count}");
                vm.ImageEffects.UpdatePreview();
            }
            else
            {
                Console.WriteLine("[TaskSettingsPanel] DataContext set to non-TaskSettingsViewModel.");
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private async void OnCustomizeEditorToolbarClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not TaskSettingsViewModel vm || TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }

            var dialog = new SurfaceWindow
            {
                Title = "Customize image editor toolbar",
                Width = 700,
                Height = 650,
                MinWidth = 520,
                MinHeight = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            dialog.Content = new ToolbarCustomizationDialogView
            {
                DataContext = new ToolbarCustomizationDialogViewModel(
                    ToolbarCustomizationItemViewModel.CreateFromOptions(vm.EditorOptions.ToolbarItems),
                    items =>
                    {
                        vm.EditorOptions.ToolbarItems = items.Select(item => item.ToOptions()).ToList();
                        dialog.Close();
                    },
                    dialog.Close)
            };
            await dialog.ShowDialog(owner);
        }
    }
}
