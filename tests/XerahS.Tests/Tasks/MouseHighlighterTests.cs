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

using System.Diagnostics;
using System.Drawing;
using NUnit.Framework;
using XerahS.Core;
using XerahS.Core.Tools;
using XerahS.Platform.Abstractions;

namespace XerahS.Tests.Tasks;

[TestFixture]
public class MouseHighlighterTests
{
    [Test]
    public void Ripple_FollowsHeldButton_AddsReleaseCrosshairs_AndExpires()
    {
        long origin = Stopwatch.GetTimestamp();
        var input = new MouseHighlighterInputBuffer(new Point(-100, 20));
        var options = new MouseHighlighterOptions { ShowPrimaryReleaseCrosshairs = true };
        var state = new MouseHighlighterState(options, input, origin);
        input.PublishButton(new(MouseHighlightButton.Primary, true, input.Position, origin));
        state.Advance(0);
        Assert.That(state.Highlights, Has.Count.EqualTo(1));
        input.SetPosition(new Point(-80, 30));
        state.Advance(10);
        Assert.That(state.Highlights[0].Position, Is.EqualTo(input.Position));
        input.PublishButton(new(MouseHighlightButton.Primary, false, input.Position, origin));
        state.Advance(20);
        Assert.That(state.Highlights, Has.Count.EqualTo(2));
        Assert.That(state.Highlights[1].Crosshairs, Is.True);
        state.Advance(options.RippleDuration);
        Assert.That(state.Highlights, Is.Empty);
    }

    [Test]
    public void DisabledButtonsAndFixedRipple_AreRespected()
    {
        long origin = Stopwatch.GetTimestamp();
        var input = new MouseHighlighterInputBuffer(new Point(2, 3));
        var state = new MouseHighlighterState(new MouseHighlighterOptions { HighlightMiddleClicks = false, FollowCursorWhileHeld = false }, input, origin);
        input.PublishButton(new(MouseHighlightButton.Middle, true, input.Position, origin));
        input.PublishButton(new(MouseHighlightButton.Secondary, true, input.Position, origin));
        state.Advance(0);
        input.SetPosition(new Point(100, 200));
        state.Advance(10);
        Assert.That(state.Highlights, Has.Count.EqualTo(1));
        Assert.That(state.Highlights[0].Position, Is.EqualTo(new Point(2, 3)));
    }

    [Test]
    public void InputOverflow_RecoversFinalButtonStateWithoutStuckHighlights()
    {
        long origin = Stopwatch.GetTimestamp();
        var input = new MouseHighlighterInputBuffer(Point.Empty);
        var state = new MouseHighlighterState(new MouseHighlighterOptions(), input, origin);
        for (int i = 0; i < 600; i++) input.PublishButton(new(MouseHighlightButton.Primary, i % 2 == 0, Point.Empty, origin));
        state.Advance(0);
        Assert.That(state.Highlights, Is.Empty);
        input.PublishButton(new(MouseHighlightButton.Primary, true, Point.Empty, origin));
        state.Advance(1);
        Assert.That(state.Highlights, Has.Count.EqualTo(1));
        input.PublishButton(new(MouseHighlightButton.Primary, false, Point.Empty, origin));
        state.Advance(500);
        Assert.That(state.Highlights, Is.Empty);
    }

    [Test]
    public void Options_ValidateImportedOutOfRangeSettings()
    {
        var options = new MouseHighlighterOptions { Mode = (MouseHighlightMode)999, Radius = -20, FadeDelay = -1, RippleDuration = 0, RippleIntensity = double.NaN };
        options.Validate();
        Assert.That(options.Mode, Is.EqualTo(MouseHighlightMode.Ripple));
        Assert.That(options.Radius, Is.EqualTo(5));
        Assert.That(options.FadeDelay, Is.Zero);
        Assert.That(options.RippleDuration, Is.EqualTo(60));
        Assert.That(options.RippleIntensity, Is.EqualTo(0.7));
    }
}
