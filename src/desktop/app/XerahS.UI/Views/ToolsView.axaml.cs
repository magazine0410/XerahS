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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using XerahS.UI.Helpers;

namespace XerahS.UI.Views
{
    /// <summary>
    /// ShareX's Tools grid (MainWindow's tool grid flyout): the tools by category. In edit mode every tool shows
    /// a switch, and clicking a tool hides it from the Tools menus or shows it again instead of opening it.
    /// </summary>
    public partial class ToolsView : PageView
    {
        private bool _isEditing;

        public ToolsView()
        {
            InitializeComponent();
            Rebuild();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            ToolMenuCatalog.HiddenToolsChanged += OnHiddenToolsChanged;
            Rebuild();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            ToolMenuCatalog.HiddenToolsChanged -= OnHiddenToolsChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnHiddenToolsChanged(object? sender, EventArgs e)
        {
            // The switches already show the change while editing.
            if (!_isEditing) Rebuild();
        }

        private void OnEditClick(object? sender, RoutedEventArgs e)
        {
            _isEditing = !_isEditing;
            Rebuild();
        }

        private void Rebuild()
        {
            if (this.FindControl<Button>("EditButton") is Button editButton)
            {
                editButton.Content = _isEditing ? "Done" : "Edit";
            }

            if (this.FindControl<StackPanel>("CategoriesPanel") is not StackPanel panel)
            {
                return;
            }

            panel.Children.Clear();

            foreach (var (category, tools) in ToolMenuCatalog.GetVisibleCategories(includeHidden: _isEditing))
            {
                if (panel.Children.Count > 0)
                {
                    panel.Children.Add(new Separator { Margin = new Thickness(0, 6) });
                }

                panel.Children.Add(new TextBlock { Text = category.Header, FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(4, 0, 0, 2) });

                var grid = new UniformGrid { Columns = 3 };
                foreach (ToolMenuItem tool in tools)
                {
                    grid.Children.Add(_isEditing ? CreateVisibilityButton(tool) : CreateToolButton(tool));
                }

                panel.Children.Add(grid);
            }
        }

        private Button CreateToolButton(ToolMenuItem tool)
        {
            var button = new Button
            {
                Content = tool.Header,
                Margin = new Thickness(3),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            Avalonia.Automation.AutomationProperties.SetName(button, tool.Header);
            button.Click += (_, _) => (TopLevel.GetTopLevel(this) as MainWindow)?.OpenTool(ToolMenuCatalog.GetLaunchTag(tool));
            return button;
        }

        private static ToggleButton CreateVisibilityButton(ToolMenuItem tool)
        {
            bool isVisible = !ToolMenuCatalog.IsHidden(tool);
            var visibilitySwitch = new ToggleSwitch
            {
                IsChecked = isVisible,
                IsHitTestVisible = false,
                Focusable = false,
                OnContent = null,
                OffContent = null,
                VerticalAlignment = VerticalAlignment.Center
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(visibilitySwitch);
            content.Children.Add(new TextBlock { Text = tool.Header, VerticalAlignment = VerticalAlignment.Center });

            var button = new ToggleButton
            {
                Content = content,
                IsChecked = isVisible,
                Margin = new Thickness(3),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            Avalonia.Automation.AutomationProperties.SetName(button, tool.Header);
            button.Click += (_, _) =>
            {
                bool showTool = button.IsChecked == true;
                visibilitySwitch.IsChecked = showTool;
                ToolMenuCatalog.SetHidden(tool, !showTool);
            };
            return button;
        }
    }
}
