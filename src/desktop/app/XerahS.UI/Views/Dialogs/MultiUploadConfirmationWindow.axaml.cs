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

namespace XerahS.UI.Views.Dialogs;

/// <summary>Result of the multi-upload confirmation. "Don't show again" applies whichever button was clicked, as in ShareX.</summary>
public readonly record struct MultiUploadConfirmationResult(bool IsConfirmed, bool DontShowAgain);

public partial class MultiUploadConfirmationWindow : Window
{
    private readonly TaskCompletionSource<MultiUploadConfirmationResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _confirmed;

    public MultiUploadConfirmationWindow() : this(1) { }

    public MultiUploadConfirmationWindow(int fileCount)
    {
        InitializeComponent();
        MessageText.Text = $"Are you sure you want to upload {fileCount} files?";
        Opened += (_, _) => Activate();
        Closed += (_, _) => _completion.TrySetResult(new(_confirmed, DontShowAgainCheckBox.IsChecked == true));
    }

    /// <summary>Completes when the window closes.</summary>
    internal Task<MultiUploadConfirmationResult> Result => _completion.Task;

    public static Task<MultiUploadConfirmationResult> ShowAsync(int fileCount)
    {
        var window = new MultiUploadConfirmationWindow(fileCount);
        window.Show();
        return window.Result;
    }

    private void OnUploadClick(object? sender, RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
