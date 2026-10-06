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

using XerahS.Platform.Abstractions;

namespace XerahS.UI.ViewModels;

public partial class SettingsViewModel
{
    public bool SupportsAdditionalShellIntegration => PlatformServices.GetShellIntegrationIfAvailable()?.SupportsIntegration(ShellIntegrationKind.ImageEditor) == true;
    private bool GetShellIntegration(ShellIntegrationKind kind) => PlatformServices.GetShellIntegrationIfAvailable()?.IsIntegrationEnabled(kind) == true;
    private void SetShellIntegration(ShellIntegrationKind kind, bool enable, string property)
    {
        bool applied = PlatformServices.GetShellIntegrationIfAvailable()?.SetIntegrationEnabled(kind, enable) == true;
        ShellIntegrationStatus = applied ? "Integration updated." : "Could not update integration. Check the log for details.";
        OnPropertyChanged(property);
        OnPropertyChanged(nameof(ShellIntegrationStatus));
    }
    public string ShellIntegrationStatus { get; private set; } = "";
    public bool EnableImageEditorIntegration
    {
        get => GetShellIntegration(ShellIntegrationKind.ImageEditor);
        set => SetShellIntegration(ShellIntegrationKind.ImageEditor, value, nameof(EnableImageEditorIntegration));
    }
    public bool RegisterCustomUploaderExtension
    {
        get => GetShellIntegration(ShellIntegrationKind.CustomUploader);
        set => SetShellIntegration(ShellIntegrationKind.CustomUploader, value, nameof(RegisterCustomUploaderExtension));
    }
    public bool RegisterImageEffectExtension
    {
        get => GetShellIntegration(ShellIntegrationKind.ImageEffect);
        set => SetShellIntegration(ShellIntegrationKind.ImageEffect, value, nameof(RegisterImageEffectExtension));
    }
    public bool EnableChromeExtension
    {
        get => GetShellIntegration(ShellIntegrationKind.Chrome);
        set => SetShellIntegration(ShellIntegrationKind.Chrome, value, nameof(EnableChromeExtension));
    }
    public bool EnableFirefoxExtension
    {
        get => GetShellIntegration(ShellIntegrationKind.Firefox);
        set => SetShellIntegration(ShellIntegrationKind.Firefox, value, nameof(EnableFirefoxExtension));
    }
}
