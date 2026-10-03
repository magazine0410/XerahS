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
using Avalonia.Interactivity;
using Avalonia.Threading;
using XerahS.Uploaders.SharingServices;

namespace XerahS.UI.Views;

/// <summary>ShareX's email window, used by the Email URL sharing service.</summary>
public partial class EmailComposeWindow : SurfaceWindow
{
    public EmailComposeWindow() : this(new EmailMessageDraft(string.Empty, string.Empty, string.Empty))
    {
    }

    public EmailComposeWindow(EmailMessageDraft draft)
    {
        InitializeComponent();
        RequestedThemeVariant = ShareX.ImageEditor.Presentation.Theming.ThemeManager.GetCurrentTheme();
        ToEmailTextBox.Text = draft.ToEmail;
        SubjectTextBox.Text = draft.Subject;
        MessageTextBox.Text = draft.Body;
        ToEmailTextBox.TextChanged += (_, _) => UpdateSendButton();
        UpdateSendButton();
        Opened += (_, _) =>
        {
            Activate();
            // As in ShareX, the message is focused with the caret after the URL.
            MessageTextBox.Focus();
            MessageTextBox.CaretIndex = MessageTextBox.Text?.Length ?? 0;
        };
    }

    /// <summary>The email to send, or null when the window was cancelled or closed.</summary>
    public EmailMessageDraft? Result { get; private set; }

    /// <summary>Shows the window and returns the email to send, or null when it is cancelled.</summary>
    public static Task<EmailMessageDraft?> ShowAsync(EmailMessageDraft draft, CancellationToken cancellationToken)
    {
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var window = new EmailComposeWindow(draft);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(window.Close));
            window.Show();
            await closed.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return window.Result;
        });
    }

    private void UpdateSendButton() => SendButton.IsEnabled = !string.IsNullOrWhiteSpace(ToEmailTextBox.Text);

    private void OnSendClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ToEmailTextBox.Text)) return;
        Result = new EmailMessageDraft(ToEmailTextBox.Text.Trim(), SubjectTextBox.Text ?? string.Empty, MessageTextBox.Text ?? string.Empty);
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
