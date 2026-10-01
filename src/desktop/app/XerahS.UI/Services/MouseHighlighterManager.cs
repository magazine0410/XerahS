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

using Avalonia.Threading;
using XerahS.Core;
using XerahS.UI.Views;

namespace XerahS.UI.Services;

internal static class MouseHighlighterManager
{
    private static MouseHighlighterService? _service;
    private static MouseHighlighterWindow? _settingsWindow;
    private static bool _shutdown;
    public static bool IsManuallyActive => _service != null;
    public static event Action? StateChanged;

    public static void ShowWindow(MouseHighlighterOptions options)
    {
        if (_shutdown) return;
        if (!WorkflowCatalog.IsAvailable(WorkflowType.MouseHighlighter))
        {
            UploadWorkflowService.ReportError(new PlatformNotSupportedException("Mouse highlighting requires Windows or X11 with global input and click-through overlay support."));
            return;
        }
        if (_settingsWindow == null)
        {
            _settingsWindow = new MouseHighlighterWindow(options, () => _ = ImageEditorOptionsStore.PersistAsync());
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        _settingsWindow.Activate();
    }

    public static void Toggle(MouseHighlighterOptions options)
    {
        try { SetManualActive(!IsManuallyActive, options); }
        catch (Exception ex) { UploadWorkflowService.ReportError(ex, "Could not start mouse highlighting"); }
    }

    internal static void SetManualActive(bool active, MouseHighlighterOptions options)
    {
        if (_shutdown) return;
        if (active)
        {
            if (!WorkflowCatalog.IsAvailable(WorkflowType.MouseHighlighter))
                throw new PlatformNotSupportedException("Global mouse highlighting is not supported on this window system.");
            if (_service == null) _service = new MouseHighlighterService(options);
            else _service.UpdateOptions(options);
        }
        else
        {
            _service?.Dispose();
            _service = null;
        }
        StateChanged?.Invoke();
    }

    public static void RefreshOptions(MouseHighlighterOptions options)
    {
        if (_service != null && ReferenceEquals(_service.Options, options)) _service.UpdateOptions(options);
    }

    public static void Shutdown()
    {
        _shutdown = true;
        _service?.Dispose();
        _service = null;
        _settingsWindow?.Close();
    }

    internal static void StopOnError(MouseHighlighterService service, Exception exception)
    {
        if (!ReferenceEquals(_service, service)) return;
        _service.Dispose();
        _service = null;
        StateChanged?.Invoke();
        UploadWorkflowService.ReportError(exception, "Mouse highlighting failed");
    }
}
