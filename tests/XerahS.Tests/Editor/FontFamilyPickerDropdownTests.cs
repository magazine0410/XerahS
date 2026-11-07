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
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using NUnit.Framework;
using ShareX.ImageEditor.Presentation.Controls;

namespace XerahS.Tests.Editor;

[TestFixture]
public class FontFamilyPickerDropdownTests
{
    // More fonts than fit in the popup, like a typical Linux or Windows install (this machine has 269).
    private static readonly string[] ManyFonts = Enumerable.Range(0, 300).Select(i => $"Font {i:D3}").ToArray();

    [AvaloniaTest]
    public void OpeningThePopup_CreatesButtonsOnlyForTheVisibleFonts()
    {
        var picker = new FontFamilyPickerDropdown { FontFamilyOptions = ManyFonts, SelectedFontFamily = ManyFonts[0] };
        var window = new Window { Width = 400, Height = 600, Content = picker };

        try
        {
            window.Show();
            Popup popup = OpenPopup(picker);

            Assert.That(GetItemsControl(popup).GetRealizedContainers().Count(), Is.LessThan(50));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void SelectedFont_IsMarkedActive_WhenScrolledIntoView()
    {
        string selected = ManyFonts[250];
        var picker = new FontFamilyPickerDropdown { FontFamilyOptions = ManyFonts, SelectedFontFamily = selected };
        var window = new Window { Width = 400, Height = 600, Content = picker };

        try
        {
            window.Show();
            Popup popup = OpenPopup(picker);
            ItemsControl itemsControl = GetItemsControl(popup);

            itemsControl.ScrollIntoView(250);
            Dispatcher.UIThread.RunJobs();

            List<Button> buttons = itemsControl.GetRealizedContainers()
                .OfType<ContentPresenter>()
                .Select(presenter => presenter.Child)
                .OfType<Button>()
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(buttons.Single(b => Equals(b.CommandParameter, selected)).Classes, Does.Contain("active"));
                Assert.That(buttons.Where(b => b.Classes.Contains("active")).Select(b => b.CommandParameter), Is.EqualTo(new[] { selected }));
            });
        }
        finally
        {
            window.Close();
        }
    }

    private static Popup OpenPopup(FontFamilyPickerDropdown picker)
    {
        Popup popup = picker.FindControl<Popup>("FontFamilyPopup")!;
        popup.IsOpen = true;
        Dispatcher.UIThread.RunJobs();
        return popup;
    }

    private static ItemsControl GetItemsControl(Popup popup)
    {
        return (ItemsControl)((ScrollViewer)((Border)popup.Child!).Child!).Content!;
    }
}
