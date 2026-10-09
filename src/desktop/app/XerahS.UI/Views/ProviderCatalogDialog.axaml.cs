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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using XerahS.UI.ViewModels;

namespace XerahS.UI.Views;

public partial class ProviderCatalogDialog : UserControl
{
    public ProviderCatalogDialog()
    {
        InitializeComponent();
        // The list handles Enter itself, so the key is taken before it reaches the list.
        ProviderList.AddHandler(KeyDownEvent, OnProviderKeyDown, RoutingStrategies.Tunnel);
    }

    public ProviderCatalogDialog(ProviderCatalogViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    // A double-click or Enter on a destination adds it, as the Add button does.
    private void OnProviderDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) != null)
        {
            AddSelected();
        }
    }

    private void OnProviderKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && AddSelected())
        {
            e.Handled = true;
        }
    }

    private bool AddSelected()
    {
        if (DataContext is not ProviderCatalogViewModel { SelectedProvider: not null } viewModel ||
            !viewModel.AddSelectedCommand.CanExecute(null))
        {
            return false;
        }

        viewModel.AddSelectedCommand.Execute(null);
        return true;
    }
}
