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
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace XerahS.UI.Views.Dialogs;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <param name="cancelText">The second button's text; null shows only the confirm button.</param>
    public ConfirmationDialog(string title, string message, string confirmText = "Yes", string? cancelText = "No")
        : this()
    {

        Title = title;
        this.FindControl<TextBlock>("DialogTitleText")!.Text = title;
        this.FindControl<TextBlock>("DialogMessageText")!.Text = message;
        var confirmButton = this.FindControl<Button>("ConfirmButton")!;
        confirmButton.Content = confirmText;
        var cancelButton = this.FindControl<Button>("CancelButton")!;
        cancelButton.Content = cancelText;
        cancelButton.IsVisible = cancelText != null;
        // A message's only button answers Enter and Escape, as in a message box.
        confirmButton.IsDefault = cancelText == null;
        confirmButton.IsCancel = cancelText == null;
    }

    /// <summary>
    /// Shows a message with an OK button in a window of its own, as ShareX's message boxes at startup, which do not need
    /// the main window (it may be hidden in the tray). It stays above the main window while that is shown. Completes when
    /// the window closes.
    /// </summary>
    public static Task ShowMessageAsync(string title, string message)
    {
        Window? mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        bool hasOwner = mainWindow is { IsVisible: true };
        var dialog = new ConfirmationDialog(title, message, "OK", cancelText: null)
        {
            WindowStartupLocation = hasOwner ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ShowInTaskbar = !hasOwner
        };
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Closed += (_, _) => closed.TrySetResult();
        if (hasOwner)
        {
            dialog.Show(mainWindow!);
        }
        else
        {
            dialog.Show();
        }

        dialog.Activate();
        return closed.Task;
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
