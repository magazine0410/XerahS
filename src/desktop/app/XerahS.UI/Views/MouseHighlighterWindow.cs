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
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using ShareX.ImageEditor.Presentation.Theming;
using XerahS.Core;
using XerahS.UI.Services;

namespace XerahS.UI.Views;

public sealed class MouseHighlighterWindow : Window
{
    private readonly MouseHighlighterOptions _options;
    private readonly Button _toggle;

    public MouseHighlighterWindow(MouseHighlighterOptions options, Action? settingsChanged)
    {
        _options = options;
        Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(new Uri("avares://XerahS.UI/"))
        {
            Source = new Uri("avares://ShareX.ImageEditor/Presentation/Theming/ImageEditorTheme.axaml")
        });
        Title = "XerahS - " + "Mouse highlighter";
        RequestedThemeVariant = ShareX.ImageEditor.Presentation.Theming.ThemeManager.GetCurrentTheme();
        Width = 620;
        Height = 690;
        MinWidth = 520;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.Bind(BackgroundProperty, new DynamicResourceExtension("ShareX.Brush.Background.Main"));
        StackPanel panel = new() { Spacing = 14, Margin = new Thickness(20) };
        _toggle = new Button();
        _toggle.Click += (_, _) => Toggle();
        panel.Children.Add(_toggle);
        panel.Children.Add(new MouseHighlighterSettingsControl(options, settingsChanged));
        Content = new ScrollViewer { Content = panel };
        MouseHighlighterManager.StateChanged += RefreshState;
        Closed += (_, _) => MouseHighlighterManager.StateChanged -= RefreshState;
        RefreshState();
    }

    private void Toggle()
    {
        try { MouseHighlighterManager.SetManualActive(!MouseHighlighterManager.IsManuallyActive, _options); }
        catch (Exception ex)
        {
            UploadWorkflowService.ReportError(ex, "Could not start mouse highlighting");
        }
    }

    private void RefreshState()
    {
        _toggle.Content = MouseHighlighterManager.IsManuallyActive ? "Stop highlighting" : "Start highlighting";
    }
}
