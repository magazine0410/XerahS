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
using Avalonia.Threading;
using XerahS.Common;
using XerahS.Platform.Abstractions;

namespace XerahS.UI.Views.Dialogs;

public partial class FileConflictWindow : SurfaceWindow
{
    private readonly string _path;
    private readonly string _uniquePath;
    private readonly TaskCompletionSource<FileConflictResolution?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FileConflictWindow() : this(Path.Combine(Path.GetTempPath(), "image.png")) { }

    public FileConflictWindow(string path)
    {
        _path = path;
        _uniquePath = FileHelpers.GetUniqueFilePath(path);
        InitializeComponent();
        ExistingPath.Text = path;
        NewName.Text = Path.GetFileNameWithoutExtension(path);
        OverwritePreview.Text = Path.GetFileName(path);
        UniqueNamePreview.Text = Path.GetFileName(_uniquePath);
        UpdateNewName();
        Opened += (_, _) => { NewName.Focus(); NewName.SelectAll(); };
        Closed += (_, _) => _completion.TrySetResult(null);
    }

    internal async Task<FileConflictResolution?> ShowAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Show();
        Activate();
        using var registration = token.Register(() => Dispatcher.UIThread.Post(Close));
        return await _completion.Task;
    }

    private void Complete(string path, bool overwrite)
    {
        _completion.TrySetResult(new FileConflictResolution(path, overwrite));
        Close();
    }

    private void OnNewNameChanged(object? sender, TextChangedEventArgs e) => UpdateNewName();

    private void UpdateNewName()
    {
        if (NewNameButton == null) return;
        string name = NewName.Text?.Trim() ?? string.Empty;
        NewNameButton.IsEnabled = name.Length > 0 && !string.Equals(name, Path.GetFileNameWithoutExtension(_path), StringComparison.OrdinalIgnoreCase);
        NewNamePreview.Text = name.Length > 0 ? name + Path.GetExtension(_path) : string.Empty;
        Error.Text = string.Empty;
    }

    private void OnNewName(object? sender, RoutedEventArgs e)
    {
        string name = NewName.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains('\\') || name is "." or "..")
        {
            Error.Text = "Enter a valid file name.";
            return;
        }
        string path = Path.Combine(Path.GetDirectoryName(_path)!, name + Path.GetExtension(_path));
        if (File.Exists(path)) { Error.Text = "A file with this name already exists."; return; }
        Complete(path, false);
    }

    private void OnOverwrite(object? sender, RoutedEventArgs e) => Complete(_path, true);
    private void OnUniqueName(object? sender, RoutedEventArgs e) => Complete(_uniquePath, false);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.Enter)
        {
            if (string.Equals(NewName.Text, Path.GetFileNameWithoutExtension(_path), StringComparison.OrdinalIgnoreCase))
                Complete(_path, true);
            else OnNewName(this, e);
            e.Handled = true;
        }
    }
}
