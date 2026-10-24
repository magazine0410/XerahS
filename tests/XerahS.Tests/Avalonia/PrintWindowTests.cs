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
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using XerahS.Common;
using XerahS.UI.ViewModels;
using XerahS.UI.Views;

namespace XerahS.Tests.Avalonia;

[TestFixture]
public sealed class PrintWindowTests
{
    [AvaloniaTest]
    public void Preview_LeavesTheFocusToThePreviewWindow()
    {
        var window = new PrintWindow(new PrintOptionsViewModel(new PrintSettings(), previewOnly: false));
        bool previewed = false;
        window.PreviewRequested = () => { previewed = true; return Task.CompletedTask; };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        int activations = 0;
        window.Activated += (_, _) => activations++;

        Button preview = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Preview..."));
        preview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.That(previewed, Is.True);
        Assert.That(activations, Is.Zero, "The preview closes when it loses the focus, so the print window must not take it back.");
        window.Close();
    }
}
