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
using Avalonia.Threading;

namespace XerahS.UI.Helpers;

/// <summary>Starts a cold-launch workflow after the main window has finished opening without taking focus.</summary>
internal static class StartupWorkflowLauncher
{
    public static void RunAfterOpened(Window window, Action run)
    {
        window.ShowActivated = false;
        bool closed = false;

        void Detach()
        {
            window.Opened -= OnOpened;
            window.Closed -= OnClosed;
        }

        void Schedule() => Dispatcher.UIThread.Post(() =>
        {
            Detach();
            if (!closed) run();
        }, DispatcherPriority.Loaded);

        void OnOpened(object? sender, EventArgs args)
        {
            window.Opened -= OnOpened;
            Schedule();
        }

        void OnClosed(object? sender, EventArgs args)
        {
            closed = true;
            Detach();
        }

        window.Closed += OnClosed;
        if (window.IsVisible) Schedule();
        else window.Opened += OnOpened;
    }
}
