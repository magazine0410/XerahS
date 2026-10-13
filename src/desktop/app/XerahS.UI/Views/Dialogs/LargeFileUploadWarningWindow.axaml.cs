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
using XerahS.Core.Tasks.Processors;

namespace XerahS.UI.Views.Dialogs;

/// <summary>ShareX's large file warning. "Continue" uploads the file; "Cancel", or closing the window, stops the task.</summary>
public partial class LargeFileUploadWarningWindow : Window
{
    private readonly TaskCompletionSource<LargeFileUploadWarningResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _continue;

    public LargeFileUploadWarningWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Activate();
        Closed += (_, _) => _completion.TrySetResult(new(_continue, DontShowAgainCheckBox.IsChecked == true));
    }

    /// <summary>Completes when the window closes.</summary>
    internal Task<LargeFileUploadWarningResult> Result => _completion.Task;

    /// <summary>Shows the warning from any thread. A stopped task closes it, which answers Cancel.</summary>
    public static Task<LargeFileUploadWarningResult> ShowAsync(CancellationToken token) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = new LargeFileUploadWarningWindow();
            using var registration = token.Register(() => Dispatcher.UIThread.Post(window.Close));
            window.Show();
            return await window.Result;
        });

    private void OnContinueClick(object? sender, RoutedEventArgs e)
    {
        _continue = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
