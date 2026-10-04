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
using XerahS.Common;

namespace XerahS.UI.Helpers;

/// <summary>
/// Startup rules for Application Settings "Start minimized to tray" (<c>SilentRun</c>).
/// </summary>
internal static class SilentRunStartupPolicy
{
    /// <summary>
    /// True when the main window should be hidden to the tray on this first <see cref="Window.Opened"/>.
    /// Subsequent tray "Open Main Window" calls must stay visible.
    /// </summary>
    public static bool ShouldHideMainWindowToTray(bool silentRunEnabled, bool isExiting, bool alreadyApplied)
    {
        return silentRunEnabled && !isExiting && !alreadyApplied;
    }

    /// <summary>
    /// As in ShareX, XerahS starts in the tray when "Start minimized to tray" is on or it was started with
    /// <c>-silent</c> (as the run-at-startup entries do), but only while the tray icon is shown; without
    /// it, the main window opens.
    /// </summary>
    public static bool StartsInTray(bool silentRunSetting, IEnumerable<string>? arguments, bool showTray)
    {
        bool silentFlag = arguments?.Any(arg => arg.Equals(AppContracts.Cli.SilentStartupFlag, StringComparison.OrdinalIgnoreCase)) == true;
        return (silentRunSetting || silentFlag) && showTray;
    }

    /// <summary>
    /// As in ShareX, closing the main window hides it to the tray while the tray icon is shown, and exits
    /// XerahS otherwise (or when it is exiting from the tray menu). Without a tray host (some Linux desktops),
    /// the icon is not shown, so closing exits instead of leaving XerahS running with no window to return to.
    /// </summary>
    public static bool HidesToTrayOnClose(bool showTray, bool isExiting, bool trayHostAvailable)
    {
        return showTray && !isExiting && trayHostAvailable;
    }

    /// <summary>
    /// Constructor / first-open navigation must not <see cref="Window.Show"/> the main window.
    /// Showing during construction fires <see cref="Window.Opened"/> before the SilentRun hide
    /// handler is attached, so the setting appears to do nothing.
    /// </summary>
    public static bool ShouldActivateWindowOnNavigate(bool suppressWindowActivation)
    {
        return !suppressWindowActivation;
    }

    public static void ApplyHiddenToTray(Window window)
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        if (window.IsVisible)
        {
            window.Hide();
        }
    }
}
