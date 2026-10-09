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
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using NUnit.Framework;
using ShareX.Vgyme.Plugin;
using XerahS.Common;
using XerahS.UI.ViewModels;
using XerahS.UI.Views;
using XerahS.Uploaders.PluginSystem;

namespace XerahS.Tests.Views;

[TestFixture, NonParallelizable]
public class ProviderCatalogDialogTests
{
    private string _folder = null!;
    private string _previousFolder = null!;

    [SetUp]
    public void SetUp()
    {
        _previousFolder = PathsManager.PersonalFolder;
        _folder = Path.Combine(Path.GetTempPath(), "xerahs-catalog-dialog-" + Guid.NewGuid().ToString("N"));
        PathsManager.PersonalFolder = _folder;
        InstanceManager.Instance.ReloadConfiguration();
        ProviderCatalog.Clear();
        ProviderCatalog.RegisterProvider(new VgymeProvider());
    }

    [TearDown]
    public void TearDown()
    {
        ProviderCatalog.Clear();
        PathsManager.PersonalFolder = _previousFolder;
        InstanceManager.Instance.ReloadConfiguration();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [AvaloniaTest]
    public void ManyDestinations_KeepTheButtonsOnA1080PixelScreen_AndEnterAddsTheSelectedOne()
    {
        var viewModel = new ProviderCatalogViewModel(UploaderCategory.Image);
        for (int i = 0; i < 30; i++)
            viewModel.AvailableProviders.Add(new ProviderViewModel(new VgymeProvider(), UploaderCategory.Image));
        List<UploaderInstance>? added = null;
        viewModel.OnInstancesAdded = instances => added = instances;
        var dialog = new ProviderCatalogDialog(viewModel);

        // As in the main window's modal host: a vertical ScrollViewer, which gives the dialog unlimited height.
        var window = new Window
        {
            Width = 1200,
            Height = 1040,
            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = dialog }
        };
        window.Show();
        window.Activate();
        viewModel.SelectedProvider = viewModel.AvailableProviders[0];
        window.UpdateLayout();
        Assert.That(dialog.DesiredSize.Height, Is.LessThanOrEqualTo(1040 - 40), "the dialog and its buttons fit, with the host's margins");
        var list = dialog.GetVisualDescendants().OfType<ListBox>().Single();
        // Clicking a destination focuses its entry.
        list.ContainerFromIndex(0)!.Focus();
        Assert.That(list.IsKeyboardFocusWithin, Is.True);
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        Assert.That(added?.Select(instance => instance.ProviderId), Is.EqualTo(new[] { "vgyme" }));
        window.Close();
    }
}
