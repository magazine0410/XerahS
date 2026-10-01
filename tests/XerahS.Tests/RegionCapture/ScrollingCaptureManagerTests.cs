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
using SkiaSharp;
using XerahS.Platform.Abstractions;
using XerahS.RegionCapture;

namespace XerahS.Tests.RegionCapture;

public class ScrollingCaptureManagerTests
{
    private const int FrameWidth = 60;
    private const int FrameHeight = 100;
    private const int FixedChromeHeight = 10;
    private const int ScrollStep = 15;
    private static readonly SKColor FixedHeaderColor = new(240, 32, 32);
    private static readonly SKColor FixedFooterColor = new(32, 32, 240);

    [Test]
    public async Task CaptureAsync_TrimsDuplicateRowsWhenFrameContainsFixedHeader()
    {
        using var firstFrame = CreateFrame(contentStartIndex: 0, fixedTopRows: FixedChromeHeight);
        using var secondFrame = CreateFrame(contentStartIndex: ScrollStep, fixedTopRows: FixedChromeHeight);

        ScrollingCaptureResult result = await RunCaptureAsync([firstFrame, secondFrame], autoIgnoreBottomEdge: false);

        try
        {
            Assert.That(result.Status, Is.EqualTo(ScrollingCaptureStatus.Successful));
            Assert.That(result.FramesCaptured, Is.EqualTo(2));
            Assert.That(result.Image, Is.Not.Null);
            Assert.That(result.Image!.Height, Is.EqualTo(FrameHeight + ScrollStep));

            for (int row = 0; row < FixedChromeHeight; row++)
            {
                AssertRowColor(result.Image, row, FixedHeaderColor);
            }

            for (int row = 0; row < FrameHeight - FixedChromeHeight + ScrollStep; row++)
            {
                AssertRowColor(result.Image, FixedChromeHeight + row, ContentColor(row));
            }
        }
        finally
        {
            result.Image?.Dispose();
        }
    }

    [Test]
    public async Task CaptureAsync_TrimsLargeOverlapWithoutDuplicatingContent()
    {
        using var firstFrame = CreateFrame(contentStartIndex: 0);
        using var secondFrame = CreateFrame(contentStartIndex: ScrollStep);

        ScrollingCaptureResult result = await RunCaptureAsync([firstFrame, secondFrame], autoIgnoreBottomEdge: false);

        try
        {
            Assert.That(result.Status, Is.EqualTo(ScrollingCaptureStatus.Successful));
            Assert.That(result.FramesCaptured, Is.EqualTo(2));
            Assert.That(result.Image, Is.Not.Null);
            Assert.That(result.Image!.Height, Is.EqualTo(FrameHeight + ScrollStep));

            for (int row = 0; row < FrameHeight + ScrollStep; row++)
            {
                AssertRowColor(result.Image, row, ContentColor(row));
            }
        }
        finally
        {
            result.Image?.Dispose();
        }
    }

    [Test]
    public async Task CaptureAsync_KeepsLatestBottomChromeOnceWhenIgnoringBottomEdge()
    {
        using var firstFrame = CreateFrame(contentStartIndex: 0, fixedBottomRows: FixedChromeHeight);
        using var secondFrame = CreateFrame(contentStartIndex: ScrollStep, fixedBottomRows: FixedChromeHeight);

        ScrollingCaptureResult result = await RunCaptureAsync([firstFrame, secondFrame], autoIgnoreBottomEdge: true);

        try
        {
            Assert.That(result.Status, Is.EqualTo(ScrollingCaptureStatus.Successful));
            Assert.That(result.FramesCaptured, Is.EqualTo(2));
            Assert.That(result.Image, Is.Not.Null);
            Assert.That(result.Image!.Height, Is.EqualTo(FrameHeight + ScrollStep));

            for (int row = 0; row < FrameHeight - FixedChromeHeight + ScrollStep; row++)
            {
                AssertRowColor(result.Image, row, ContentColor(row));
            }

            for (int row = 0; row < FixedChromeHeight; row++)
            {
                AssertRowColor(result.Image, result.Image.Height - FixedChromeHeight + row, FixedFooterColor);
            }
        }
        finally
        {
            result.Image?.Dispose();
        }
    }

    [Test]
    public async Task CaptureAsync_ContinuesUntilScrollInfoReportsBottom()
    {
        using var firstFrame = CreateFrame(contentStartIndex: 0);
        using var secondFrame = CreateFrame(contentStartIndex: ScrollStep);
        using var thirdFrame = CreateFrame(contentStartIndex: ScrollStep * 2);

        ScrollingCaptureResult result = await RunCaptureAsync(
            [firstFrame, secondFrame, thirdFrame],
            autoIgnoreBottomEdge: false,
            scrollingCaptureService: new SequenceScrollingCaptureService(
                new ScrollBarInfo(30, 0, 100, 10),
                new ScrollBarInfo(91, 0, 100, 10)));

        try
        {
            Assert.That(result.Status, Is.EqualTo(ScrollingCaptureStatus.Successful));
            Assert.That(result.FramesCaptured, Is.EqualTo(3));
            Assert.That(result.Image, Is.Not.Null);
            Assert.That(result.Image!.Height, Is.EqualTo(FrameHeight + (ScrollStep * 2)));

            for (int row = 0; row < FrameHeight + (ScrollStep * 2); row++)
            {
                AssertRowColor(result.Image, row, ContentColor(row));
            }
        }
        finally
        {
            result.Image?.Dispose();
        }
    }

    [Test]
    public async Task CaptureAsync_UsesLeftBiasedScrollAnchorForWheelScrolling()
    {
        using var firstFrame = CreateFrame(contentStartIndex: 0);
        using var secondFrame = CreateFrame(contentStartIndex: ScrollStep);
        var scrollingCaptureService = new TrackingScrollingCaptureService();

        ScrollingCaptureResult result = await RunCaptureAsync(
            [firstFrame, secondFrame],
            autoIgnoreBottomEdge: false,
            scrollingCaptureService: scrollingCaptureService);

        try
        {
            Assert.That(result.Status, Is.EqualTo(ScrollingCaptureStatus.Successful));
            Assert.That(scrollingCaptureService.LastScrollTargetPoint, Is.EqualTo(new Point(18, 50)));
        }
        finally
        {
            result.Image?.Dispose();
        }
    }

    private static async Task<ScrollingCaptureResult> RunCaptureAsync(
        IReadOnlyList<SKBitmap> frames,
        bool autoIgnoreBottomEdge,
        IScrollingCaptureService? scrollingCaptureService = null)
    {
        var manager = new ScrollingCaptureManager(
            scrollingCaptureService ?? new StubScrollingCaptureService(),
            new StubScreenCaptureService(frames),
            new StubWindowService());

        return await manager.CaptureAsync(
            windowHandle: IntPtr.Zero,
            captureRegion: new SKRect(0, 0, FrameWidth, FrameHeight),
            scrollMethod: ScrollMethod.MouseWheel,
            scrollAmount: 1,
            startDelayMs: 0,
            scrollDelayMs: 0,
            autoScrollTop: false,
            autoIgnoreBottomEdge: autoIgnoreBottomEdge);
    }

    [Test]
    public async Task CaptureAsync_CapturesNothing_WhenScrollInputIsRefused()
    {
        using var frame = CreateFrame(contentStartIndex: 0);
        var scroll = new SessionScrollingCaptureService(begins: false);
        var capture = new StubScreenCaptureService([frame]);
        var manager = new ScrollingCaptureManager(scroll, capture, new StubWindowService());

        ScrollingCaptureResult result = await manager.CaptureAsync(IntPtr.Zero, new SKRect(0, 0, FrameWidth, FrameHeight), ScrollMethod.MouseWheel,
            startDelayMs: 0, scrollDelayMs: 0, autoScrollTop: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.InputUnavailable, Is.True);
            Assert.That(result.Status, Is.EqualTo(ScrollingCaptureStatus.Failed));
            Assert.That(result.FramesCaptured, Is.Zero);
            Assert.That(result.Image, Is.Null);
            Assert.That(scroll.Calls, Is.EqualTo(new[] { "Begin" }), "Nothing is scrolled, and there is no session to end.");
        });
    }

    [Test]
    public async Task CaptureAsync_FollowsShareXsOrder_AndEndsTheInputSession()
    {
        using var first = CreateFrame(contentStartIndex: 0);
        using var second = CreateFrame(contentStartIndex: ScrollStep);
        var scroll = new SessionScrollingCaptureService(begins: true);
        var capture = new StubScreenCaptureService([first, second, second]);
        var windows = new ActivationTrackingWindowService(scroll.Calls);
        var manager = new ScrollingCaptureManager(scroll, capture, windows);

        ScrollingCaptureResult result = await manager.CaptureAsync(new IntPtr(42), new SKRect(0, 0, FrameWidth, FrameHeight), ScrollMethod.MouseWheel,
            startDelayMs: 0, scrollDelayMs: 0, autoScrollTop: true, autoIgnoreBottomEdge: false);

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(scroll.Calls.Take(6), Is.EqualTo(new[] { "Begin", "Activate 42", "ScrollToTop", "Park", "Wait", "Scroll" }),
                    "As in ShareX: activate, wait the start delay, then scroll to the top; the pointer waits outside the area.");
                var afterScrolls = scroll.Calls.Select((call, index) => (call, index)).Where(entry => entry.call == "Scroll")
                    .Select(entry => scroll.Calls[entry.index + 1]);
                Assert.That(afterScrolls, Has.All.EqualTo("Park"), "Every scroll is followed by moving the pointer out of the area.");
                Assert.That(scroll.Calls.Count(call => call == "Wait"), Is.EqualTo(capture.CaptureStartTimes.Count),
                    "Each frame waits until no popup covers the area.");
                Assert.That(scroll.Calls.Last(), Is.EqualTo("End"));
                Assert.That(scroll.Calls.Count(call => call == "End"), Is.EqualTo(1));
                Assert.That(capture.LastOptions?.ShowCursor, Is.False, "As in ShareX, the frames leave out the cursor.");
                Assert.That(result.Image!.Height, Is.EqualTo(FrameHeight + ScrollStep));
            });
        }
        finally
        {
            result.Image?.Dispose();
        }
    }

    [Test]
    public async Task CaptureAsync_CountsTheScrollDelayFromTheScroll_WhenCapturingIsSlow()
    {
        using var first = CreateFrame(contentStartIndex: 0);
        using var second = CreateFrame(contentStartIndex: ScrollStep);
        var clock = Stopwatch.StartNew();
        var scroll = new SessionScrollingCaptureService(begins: true, clock);
        var capture = new StubScreenCaptureService([first, second, second], captureDelayMs: 150, clock);
        var manager = new ScrollingCaptureManager(scroll, capture, new StubWindowService());

        ScrollingCaptureResult result = await manager.CaptureAsync(IntPtr.Zero, new SKRect(0, 0, FrameWidth, FrameHeight), ScrollMethod.MouseWheel,
            startDelayMs: 0, scrollDelayMs: 300, autoIgnoreBottomEdge: false);
        result.Image?.Dispose();

        // Before, the delay also counted the slow capture, which left the page no time to settle.
        Assert.That(capture.CaptureStartTimes[1] - scroll.ScrollTimes[0], Is.GreaterThanOrEqualTo(280),
            "The next frame is taken a full scroll delay after the scroll.");
    }

    private static SKBitmap CreateFrame(int contentStartIndex, int fixedTopRows = 0, int fixedBottomRows = 0)
    {
        int contentRows = FrameHeight - fixedTopRows - fixedBottomRows;
        var bitmap = new SKBitmap(FrameWidth, FrameHeight);

        for (int y = 0; y < FrameHeight; y++)
        {
            SKColor color =
                y < fixedTopRows ? FixedHeaderColor :
                y >= FrameHeight - fixedBottomRows ? FixedFooterColor :
                ContentColor(contentStartIndex + y - fixedTopRows);

            for (int x = 0; x < FrameWidth; x++)
            {
                bitmap.SetPixel(x, y, color);
            }
        }

        Assert.That(contentRows, Is.GreaterThan(0));
        return bitmap;
    }

    private static SKColor ContentColor(int index)
    {
        return new SKColor(
            (byte)((index * 17 + 11) % 251),
            (byte)((index * 29 + 37) % 251),
            (byte)((index * 43 + 71) % 251));
    }

    private static void AssertRowColor(SKBitmap bitmap, int row, SKColor expectedColor)
    {
        int sampleX = bitmap.Width / 2;
        Assert.That(bitmap.GetPixel(sampleX, row), Is.EqualTo(expectedColor), $"Unexpected pixel at row {row}.");
    }

    private sealed class StubScrollingCaptureService : IScrollingCaptureService
    {
        public bool IsSupported => true;

        public ScrollBarInfo? GetScrollBarInfo(IntPtr windowHandle)
        {
            return new ScrollBarInfo(91, 0, 100, 10);
        }

        public Task ScrollWindowAsync(IntPtr windowHandle, ScrollMethod method, int amount, Point? targetPoint = null)
        {
            return Task.CompletedTask;
        }

        public Task ScrollToTopAsync(IntPtr windowHandle, Point? targetPoint = null)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SequenceScrollingCaptureService(params ScrollBarInfo[] scrollInfos) : IScrollingCaptureService
    {
        private readonly Queue<ScrollBarInfo> _scrollInfos = new(scrollInfos);
        private ScrollBarInfo? _lastScrollInfo;

        public bool IsSupported => true;

        public ScrollBarInfo? GetScrollBarInfo(IntPtr windowHandle)
        {
            if (_scrollInfos.Count > 0)
            {
                _lastScrollInfo = _scrollInfos.Dequeue();
            }

            return _lastScrollInfo;
        }

        public Task ScrollWindowAsync(IntPtr windowHandle, ScrollMethod method, int amount, Point? targetPoint = null)
        {
            return Task.CompletedTask;
        }

        public Task ScrollToTopAsync(IntPtr windowHandle, Point? targetPoint = null)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingScrollingCaptureService : IScrollingCaptureService
    {
        public bool IsSupported => true;

        public Point? LastScrollTargetPoint { get; private set; }

        public ScrollBarInfo? GetScrollBarInfo(IntPtr windowHandle)
        {
            return new ScrollBarInfo(91, 0, 100, 10);
        }

        public Task ScrollWindowAsync(IntPtr windowHandle, ScrollMethod method, int amount, Point? targetPoint = null)
        {
            LastScrollTargetPoint = targetPoint;
            return Task.CompletedTask;
        }

        public Task ScrollToTopAsync(IntPtr windowHandle, Point? targetPoint = null)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SessionScrollingCaptureService(bool begins, Stopwatch? clock = null) : IScrollingCaptureService
    {
        public List<string> Calls { get; } = [];
        public List<long> ScrollTimes { get; } = [];

        public Task MovePointerOutsideAsync(Rectangle area)
        {
            Calls.Add("Park");
            return Task.CompletedTask;
        }

        public Task WaitUntilAreaIsClearAsync(Rectangle area, CancellationToken cancellationToken = default)
        {
            Calls.Add("Wait");
            return Task.CompletedTask;
        }

        public bool IsSupported => true;

        public Task<bool> BeginAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("Begin");
            return Task.FromResult(begins);
        }

        public Task EndAsync()
        {
            Calls.Add("End");
            return Task.CompletedTask;
        }

        public ScrollBarInfo? GetScrollBarInfo(IntPtr windowHandle) => null;

        public Task ScrollWindowAsync(IntPtr windowHandle, ScrollMethod method, int amount, Point? targetPoint = null)
        {
            Calls.Add("Scroll");
            ScrollTimes.Add(clock?.ElapsedMilliseconds ?? 0);
            return Task.CompletedTask;
        }

        public Task ScrollToTopAsync(IntPtr windowHandle, Point? targetPoint = null)
        {
            Calls.Add("ScrollToTop");
            return Task.CompletedTask;
        }
    }

    private sealed class ActivationTrackingWindowService(List<string> calls) : IWindowService
    {
        private readonly StubWindowService _inner = new();
        public IntPtr GetForegroundWindow() => IntPtr.Zero;
        public bool SetForegroundWindow(IntPtr handle) => true;
        public string GetWindowText(IntPtr handle) => string.Empty;
        public string GetWindowClassName(IntPtr handle) => string.Empty;
        public Rectangle GetWindowBounds(IntPtr handle) => Rectangle.Empty;
        public Rectangle GetWindowClientBounds(IntPtr handle) => Rectangle.Empty;
        public bool IsWindowVisible(IntPtr handle) => true;
        public bool IsWindowMaximized(IntPtr handle) => false;
        public bool IsWindowMinimized(IntPtr handle) => false;
        public bool ShowWindow(IntPtr handle, int cmdShow) => true;
        public bool SetWindowPos(IntPtr handle, IntPtr handleInsertAfter, int x, int y, int width, int height, uint flags) => true;
        public WindowInfo[] GetAllWindows() => _inner.GetAllWindows();
        public uint GetWindowProcessId(IntPtr handle) => 0;
        public IntPtr SearchWindow(string windowTitle) => IntPtr.Zero;
        public bool SetWindowClickThrough(IntPtr handle) => true;

        public bool ActivateWindow(IntPtr handle)
        {
            calls.Add($"Activate {handle}");
            return true;
        }
    }

    private sealed class StubScreenCaptureService(IReadOnlyList<SKBitmap> frames, int captureDelayMs = 0, Stopwatch? clock = null) : IScreenCaptureService
    {
        public List<long> CaptureStartTimes { get; } = [];

        private readonly IReadOnlyList<SKBitmap> _frames = frames;
        private int _captureIndex;

        public Task<SKRectI> SelectRegionAsync(CaptureOptions? options = null)
        {
            throw new NotSupportedException();
        }

        public Task<SKBitmap?> CaptureRegionAsync(CaptureOptions? options = null)
        {
            throw new NotSupportedException();
        }

        public CaptureOptions? LastOptions { get; private set; }

        public async Task<SKBitmap?> CaptureRectAsync(SKRect rect, CaptureOptions? options = null)
        {
            LastOptions = options;
            CaptureStartTimes.Add(clock?.ElapsedMilliseconds ?? 0);
            if (captureDelayMs > 0)
            {
                await Task.Delay(captureDelayMs);
            }

            int index = Math.Min(_captureIndex, _frames.Count - 1);
            _captureIndex++;
            return _frames[index].Copy();
        }

        public Task<SKBitmap?> CaptureFullScreenAsync(CaptureOptions? options = null)
        {
            throw new NotSupportedException();
        }

        public Task<SKBitmap?> CaptureActiveWindowAsync(IWindowService windowService, CaptureOptions? options = null)
        {
            throw new NotSupportedException();
        }

        public Task<SKBitmap?> CaptureWindowAsync(IntPtr windowHandle, IWindowService windowService, CaptureOptions? options = null)
        {
            throw new NotSupportedException();
        }

        public Task<CursorInfo?> CaptureCursorAsync()
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubWindowService : IWindowService
    {
        public IntPtr GetForegroundWindow() => IntPtr.Zero;

        public bool SetForegroundWindow(IntPtr handle) => true;

        public string GetWindowText(IntPtr handle) => string.Empty;

        public string GetWindowClassName(IntPtr handle) => string.Empty;

        public Rectangle GetWindowBounds(IntPtr handle) => Rectangle.Empty;

        public Rectangle GetWindowClientBounds(IntPtr handle) => Rectangle.Empty;

        public bool IsWindowVisible(IntPtr handle) => true;

        public bool IsWindowMaximized(IntPtr handle) => false;

        public bool IsWindowMinimized(IntPtr handle) => false;

        public bool ShowWindow(IntPtr handle, int cmdShow) => true;

        public bool SetWindowPos(IntPtr handle, IntPtr handleInsertAfter, int x, int y, int width, int height, uint flags) => true;

        public WindowInfo[] GetAllWindows() => [];

        public uint GetWindowProcessId(IntPtr handle) => 0;

        public IntPtr SearchWindow(string windowTitle) => IntPtr.Zero;

        public bool ActivateWindow(IntPtr handle) => true;

        public bool SetWindowClickThrough(IntPtr handle) => true;
    }
}
