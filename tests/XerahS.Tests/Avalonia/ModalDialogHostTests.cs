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

using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using NUnit.Framework;
using ShareX.ImageEditor.Presentation.ViewModels;
using XerahS.UI.Services;

namespace XerahS.Tests.Avalonia;

// The workflow editor and the image effect browser it opens share the main window's single modal slot.
[TestFixture, NonParallelizable]
public class ModalDialogHostTests
{
    [AvaloniaTest]
    public void DialogOpenedFromADialog_GivesTheSlotBackWhenItCloses()
    {
        var host = new MainViewModel();
        object editor = new(), browser = new();
        Action<bool>? closeEditor = null, closeBrowser = null;

        var editorTask = ModalDialogHost.ShowAsync(editor, set => closeEditor = set, dismissResult: false, "test");
        Dispatcher.UIThread.RunJobs();
        var browserTask = ModalDialogHost.ShowAsync(browser, set => closeBrowser = set, dismissResult: true, "test");
        Dispatcher.UIThread.RunJobs();
        Assert.That(host.ModalContent, Is.SameAs(browser));

        closeBrowser!(true);
        Dispatcher.UIThread.RunJobs();

        Assert.Multiple(() =>
        {
            Assert.That(browserTask.IsCompletedSuccessfully, Is.True);
            Assert.That(editorTask.IsCompleted, Is.False, "Closing the browser must not close the editor.");
            Assert.That(host.ModalContent, Is.SameAs(editor));
            Assert.That(host.IsModalOpen, Is.True);
        });

        closeEditor!(true);
        Dispatcher.UIThread.RunJobs();
        Assert.That(editorTask.Result, Is.True);
        Assert.That(host.IsModalOpen, Is.False);
    }

    [AvaloniaTest]
    public void DismissingTheInnerDialog_KeepsTheOuterDialogOpen()
    {
        var host = new MainViewModel();
        object editor = new(), browser = new();
        Action<bool>? closeEditor = null;

        var editorTask = ModalDialogHost.ShowAsync(editor, set => closeEditor = set, dismissResult: false, "test");
        Dispatcher.UIThread.RunJobs();
        var browserTask = ModalDialogHost.ShowAsync(browser, _ => { }, dismissResult: true, "test");
        Dispatcher.UIThread.RunJobs();

        // The backdrop and Escape close the overlay this way.
        host.CloseModalCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Multiple(() =>
        {
            Assert.That(browserTask.Result, Is.True);
            Assert.That(editorTask.IsCompleted, Is.False);
            Assert.That(host.ModalContent, Is.SameAs(editor));
            Assert.That(host.IsModalOpen, Is.True);
        });

        host.CloseModalCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.That(editorTask.Result, Is.False);
        Assert.That(host.IsModalOpen, Is.False);
        Assert.That(closeEditor, Is.Not.Null);
    }

    [AvaloniaTest]
    public void SingleDialog_ClosesTheOverlay()
    {
        var host = new MainViewModel();
        object dialog = new();
        Action<bool>? close = null;

        var task = ModalDialogHost.ShowAsync(dialog, set => close = set, dismissResult: false, "test");
        Dispatcher.UIThread.RunJobs();
        close!(true);
        Dispatcher.UIThread.RunJobs();

        Assert.That(task.Result, Is.True);
        Assert.That(host.IsModalOpen, Is.False);
        Assert.That(host.ModalContent, Is.Null);
    }
}
