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
using Avalonia.Input;
using Avalonia.Interactivity;

namespace XerahS.UI.Views;

public partial class UploadInputWindow : SurfaceWindow
{
    private readonly bool _shortenUrl;
    private readonly TaskCompletionSource<string?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public UploadInputWindow() : this(false) { }

    public UploadInputWindow(bool shortenUrl, string? initialText = null)
    {
        InitializeComponent();
        _shortenUrl = shortenUrl;
        Title = shortenUrl ? "Shorten URL" : "Upload text";
        InputLabel.Text = shortenUrl ? "URL to shorten" : "Text to upload";
        Input.Text = initialText ?? string.Empty;
        Input.AcceptsReturn = !shortenUrl;
        Input.AcceptsTab = !shortenUrl;
        Input.TextWrapping = shortenUrl ? Avalonia.Media.TextWrapping.NoWrap : Avalonia.Media.TextWrapping.Wrap;
        Input.MinHeight = shortenUrl ? 36 : 240;
        SubmitButton.Content = shortenUrl ? "Shorten" : "Upload";
        SubmitButton.IsDefault = shortenUrl;
        Height = shortenUrl ? 240 : 450;
        // As in ShareX's text upload window: count the characters and select pre-filled text.
        CharacterCount.IsVisible = !shortenUrl;
        UpdateCharacterCount();
        Input.TextChanged += (_, _) => UpdateCharacterCount();
        Opened += (_, _) =>
        {
            Input.Focus();
            if (!shortenUrl && !string.IsNullOrEmpty(Input.Text)) Input.SelectAll();
        };
        Closed += (_, _) => _completion.TrySetResult(null);
    }

    internal static string FormatCharacterCount(int length) =>
        string.Format(length == 1 ? "{0:N0} character" : "{0:N0} characters", length);

    private void UpdateCharacterCount() => CharacterCount.Text = FormatCharacterCount(Input.Text?.Length ?? 0);

    public Task<string?> ShowAsync(Window? owner)
    {
        if (owner?.IsVisible == true && owner.WindowState != WindowState.Minimized) Show(owner);
        else Show();
        Activate();
        return _completion.Task;
    }

    internal static bool IsValidUrl(string? text) =>
        Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && !string.IsNullOrEmpty(uri.Host);

    private void OnSubmit(object? sender, RoutedEventArgs e) => Submit();
    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void Submit()
    {
        string text = Input.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) || (_shortenUrl && !IsValidUrl(text)))
        {
            Validation.Text = _shortenUrl ? "Enter a valid HTTP or HTTPS URL." : "Enter some text to upload.";
            Validation.IsVisible = true;
            Input.Focus();
            return;
        }
        _completion.TrySetResult(_shortenUrl ? text.Trim() : text);
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.Enter && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            Submit();
            e.Handled = true;
        }
    }
}
