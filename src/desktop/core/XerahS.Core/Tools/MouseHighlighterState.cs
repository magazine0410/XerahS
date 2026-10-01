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
using XerahS.Platform.Abstractions;
using DrawingPoint = System.Drawing.Point;

namespace XerahS.Core.Tools;

public sealed class MouseHighlight
{
    public DrawingPoint Position { get; set; }
    public MouseHighlightButton Button { get; init; }
    public double Started { get; init; }
    public double? Released { get; set; }
    public bool Crosshairs { get; init; }
}

/// <summary>ShareX's bounded highlight animation state, independent of native input and rendering.</summary>
public sealed class MouseHighlighterState
{
    private readonly MouseHighlighterInputBuffer _input;
    private readonly long _startTimestamp;
    private readonly List<MouseHighlight> _highlights = [];
    private readonly MouseHighlight?[] _heldHighlights = new MouseHighlight?[3];
    public MouseHighlighterOptions Options { get; private set; }
    public DrawingPoint CursorPosition { get; private set; }
    public IReadOnlyList<MouseHighlight> Highlights => _highlights;
    public double Time { get; private set; }

    public MouseHighlighterState(MouseHighlighterOptions options, MouseHighlighterInputBuffer input, long startTimestamp)
    {
        Options = options;
        options.Validate();
        _input = input;
        _startTimestamp = startTimestamp;
        CursorPosition = input.Position;
    }

    public void UpdateOptions(MouseHighlighterOptions options)
    {
        options.Validate();
        Options = options;
        _highlights.Clear();
        Array.Clear(_heldHighlights);
        _input.DiscardPendingEvents();
    }

    public void Advance(double time)
    {
        Time = time;
        ProcessPendingInput();
        _highlights.RemoveAll(highlight => highlight.Released.HasValue &&
            time - highlight.Released.Value >= (Options.Mode == MouseHighlightMode.Ripple
                ? Options.RippleDuration : Options.FadeDelay + Options.FadeDuration));
        // Bound retained effects even under very high click rates.
        while (_highlights.Count > 64)
        {
            int index = _highlights.FindIndex(highlight => highlight.Released.HasValue);
            if (index < 0) break;
            _highlights.RemoveAt(index);
        }
    }

    private void MoveCursor(DrawingPoint point)
    {
        CursorPosition = point;
        bool follow = Options.Mode != MouseHighlightMode.Ripple || Options.FollowCursorWhileHeld;
        if (follow)
        {
            foreach (MouseHighlight? highlight in _heldHighlights)
            {
                if (highlight != null) highlight.Position = point;
            }
        }
    }

    private void ProcessPendingInput()
    {
        if (_input.ConsumeOverflow())
        {
            _input.DiscardPendingEvents();
            _highlights.Clear();
            Array.Clear(_heldHighlights);
            MoveCursor(_input.Position);
            int pressed = _input.PressedButtons;
            for (int i = 0; i < _heldHighlights.Length; i++)
            {
                if ((pressed & (1 << i)) != 0) Press((MouseHighlightButton)i, CursorPosition, Time);
            }
        }
        // Limit work per frame even if input arrives continuously.
        for (int i = 0; i < MouseHighlighterInputBuffer.Capacity && _input.TryRead(out MouseHighlighterButtonEvent input); i++)
        {
            MoveCursor(input.Position);
            double time = Stopwatch.GetElapsedTime(_startTimestamp, input.Timestamp).TotalMilliseconds;
            if (input.Pressed) Press(input.Button, input.Position, time);
            else Release(input.Button, input.Position, time);
        }
        MoveCursor(_input.Position);
    }

    private void Press(MouseHighlightButton button, DrawingPoint point, double time)
    {
        if (!Options.IsButtonEnabled(button)) return;
        MouseHighlight? previous = _heldHighlights[(int)button];
        if (previous != null) previous.Released = time;
        MouseHighlight highlight = new() { Position = point, Button = button, Started = time };
        _highlights.Add(highlight);
        _heldHighlights[(int)button] = highlight;
    }

    private void Release(MouseHighlightButton button, DrawingPoint point, double time)
    {
        MouseHighlight? highlight = _heldHighlights[(int)button];
        if (highlight == null) return;
        highlight.Released = time;
        _heldHighlights[(int)button] = null;

        if (Options.Mode == MouseHighlightMode.Ripple && Options.ShowReleaseCrosshairs(button))
        {
            _highlights.Add(new MouseHighlight
            {
                Position = point,
                Button = button,
                Started = time,
                Released = time,
                Crosshairs = true
            });
        }
    }

}
