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

using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using NUnit.Framework;
using XerahS.Core;
using XerahS.Core.Hotkeys;
using XerahS.Platform.Abstractions;
using XerahS.UI.ViewModels;
using XerahS.UI.Views.Controls;

namespace XerahS.Tests.Avalonia;

[TestFixture]
public class SysRqWarningTests
{
    private static SysRqWarning ShowWorkflowRow(Key key, KeyModifiers modifiers)
    {
        var viewModel = new HotkeyItemViewModel(new WorkflowSettings(WorkflowType.RectangleRegion, new HotkeyInfo(key, modifiers)));
        var control = new HotkeySelectionControl { DataContext = viewModel };
        var window = new Window { Content = control };
        window.Show();
        return control.GetLogicalDescendants().OfType<SysRqWarning>().Single();
    }

    [AvaloniaTest]
    public void WorkflowRow_ShowsWarningOnlyForAltPrintScreen()
    {
        Assume.That(OperatingSystem.IsLinux(), "The warning is Linux-only.");

        Assert.Multiple(() =>
        {
            Assert.That(ShowWorkflowRow(Key.PrintScreen, KeyModifiers.Alt).IsVisible, Is.True);
            Assert.That(ShowWorkflowRow(Key.Print, KeyModifiers.Alt | KeyModifiers.Shift).IsVisible, Is.True);
            Assert.That(ShowWorkflowRow(Key.PrintScreen, KeyModifiers.Meta).IsVisible, Is.False);
        });
    }

    [AvaloniaTest]
    public void Warning_LinksSysRqToWikipedia()
    {
        var warning = new SysRqWarning();
        var link = warning.GetLogicalDescendants().OfType<HyperlinkButton>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(link.Content, Is.EqualTo("SysRq"));
            Assert.That(link.NavigateUri, Is.EqualTo(new Uri("https://en.wikipedia.org/wiki/Magic_SysRq_key")));
        });
    }
}
