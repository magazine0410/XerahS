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

using Avalonia.Markup.Xaml;
using XerahS.UI.Services;

namespace XerahS.UI.Views
{
    /// <summary>
    /// Settings &gt; Task Settings: edits <c>SettingsManager.DefaultTaskSettings</c> directly.
    /// </summary>
    public partial class DefaultTaskSettingsView : PageView
    {
        public DefaultTaskSettingsView()
        {
            InitializeComponent();
            DataContext = UiViewModelFactoryAccessor.GetRequired().CreateDefaultTaskSettingsViewModel();

            // The default task settings live in the workflows file. Hotkeys and menus do not depend on
            // them, so the save does not raise SettingsChanged.
            Unloaded += (_, _) => _ = ImageEditorOptionsStore.PersistAsync();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
