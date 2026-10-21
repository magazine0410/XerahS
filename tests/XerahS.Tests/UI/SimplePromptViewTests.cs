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
using Avalonia.Threading;
using NUnit.Framework;
using XerahS.UI.ViewModels;
using XerahS.UI.Views;

namespace XerahS.Tests.UI;

/// <summary>The shared prompt takes the keyboard focus when it opens, as ShareX's prompts do.</summary>
public sealed class SimplePromptViewTests
{
    [AvaloniaTest]
    public void InputPrompt_FocusesTheTextBoxWithItsTextSelected()
    {
        var vm = new SimplePromptViewModel { Title = "Rename file", Label = "Enter a new file name.", ShowInput = true, ShowCancel = true, InputText = "alpha" };
        var view = new SimplePromptView { DataContext = vm };
        var window = CreateWindow(view);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var box = view.FindControl<TextBox>("InputTextBox")!;
            Assert.Multiple(() =>
            {
                Assert.That(box.IsFocused, Is.True, "Typing must go to the text box without a click.");
                Assert.That(box.SelectedText, Is.EqualTo("alpha"), "The current text is selected, so typing replaces it.");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void ConfirmationPrompt_FocusesTheDefaultButton()
    {
        var vm = new SimplePromptViewModel { Title = "Delete history items?", Message = "Do you really want to delete this item?", ShowCancel = true };
        var view = new SimplePromptView { DataContext = vm };
        var window = CreateWindow(view);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.That(view.FindControl<Button>("PrimaryButton")!.IsFocused, Is.True, "Enter must press OK.");
        }
        finally
        {
            window.Close();
        }
    }

    // The test application has no theme; the prompt's text styles come from the shared resources.
    private static Window CreateWindow(SimplePromptView view)
    {
        var window = new Window { Width = 600, Height = 400, Content = view };
        foreach (string theme in new[] { "Typography", "ThemeResources" })
        {
            window.Styles.Add(new global::Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://XerahS.UI/"))
            {
                Source = new Uri($"avares://XerahS.UI/Themes/{theme}.axaml")
            });
        }

        return window;
    }
}
