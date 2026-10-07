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
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using NUnit.Framework;
using XerahS.UI.Controls;

namespace XerahS.Tests.Views;

[TestFixture]
public class PropertyGridTests
{
    public enum Mode { First, Second }

    public class Target
    {
        public bool Flag { get; set; }
        public Mode Choice { get; set; }
    }

    [AvaloniaTest]
    public void PropertyValueChanged_SeesTheNewValue()
    {
        // Application Settings > Advanced saves and applies the settings from this event.
        var target = new Target();
        var grid = new PropertyGrid { SelectedObject = target };
        var window = new Window { Content = grid };
        var seen = new List<(bool Flag, Mode Choice)>();
        try
        {
            window.Show();
            grid.PropertyValueChanged += (_, _) => seen.Add((target.Flag, target.Choice));

            window.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;
            window.GetVisualDescendants().OfType<ComboBox>().Single().SelectedItem = Mode.Second;
            window.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = false;

            Assert.That(seen, Is.EqualTo(new[] { (true, Mode.First), (true, Mode.Second), (false, Mode.Second) }));
        }
        finally
        {
            window.Close();
        }
    }
}
