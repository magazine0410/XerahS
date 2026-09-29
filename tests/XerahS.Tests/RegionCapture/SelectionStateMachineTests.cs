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

using NUnit.Framework;
using XerahS.RegionCapture.Models;
using XerahS.RegionCapture.Services;

namespace XerahS.Tests.RegionCapture;

public class SelectionStateMachineTests
{
    [Test]
    public void SetModifiers_DuringDrag_RecalculatesSelectionWhenAspectRatioLockIsReleased()
    {
        var stateMachine = new SelectionStateMachine();
        stateMachine.BeginDrag(new PixelPoint(0, 0));
        stateMachine.UpdateCursorPosition(new PixelPoint(10, 20));

        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(0, 0, 10, 20)));

        stateMachine.SetModifiers(SelectionModifier.LockAspectRatio);

        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(0, 0, 20, 20)));

        stateMachine.SetModifiers(SelectionModifier.None);

        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(0, 0, 10, 20)));
    }

    [Test]
    public void EndDrag_WhenQuickCropDisabled_StaysSelectedInsteadOfConfirming()
    {
        var stateMachine = new SelectionStateMachine(quickCrop: false);
        bool confirmed = false;
        stateMachine.SelectionConfirmed += _ => confirmed = true;

        stateMachine.BeginDrag(new PixelPoint(0, 0));
        stateMachine.UpdateCursorPosition(new PixelPoint(40, 30));
        stateMachine.EndDrag();

        Assert.That(confirmed, Is.False);
        Assert.That(stateMachine.CurrentState, Is.EqualTo(CaptureState.Selected));
        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(0, 0, 40, 30)));
    }

    [Test]
    public void TryConfirm_WhenSelected_RaisesSelectionConfirmed()
    {
        var stateMachine = new SelectionStateMachine(quickCrop: false);
        RegionSelectionResult? result = null;
        stateMachine.SelectionConfirmed += value => result = value;

        stateMachine.BeginDrag(new PixelPoint(10, 20));
        stateMachine.UpdateCursorPosition(new PixelPoint(50, 80));
        stateMachine.EndDrag();

        Assert.That(stateMachine.TryConfirm(), Is.True);
        Assert.That(stateMachine.CurrentState, Is.EqualTo(CaptureState.Confirmed));
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Region, Is.EqualTo(new PixelRect(10, 20, 40, 60)));
    }

    [Test]
    public void UpdateCursorPosition_WhenNearSnapSize_SnapsDragRectangle()
    {
        var stateMachine = new SelectionStateMachine(
            quickCrop: false,
            snapSizes: [new CaptureSnapSize(100, 50)],
            snapDistance: 30);

        stateMachine.BeginDrag(new PixelPoint(0, 0));
        stateMachine.UpdateCursorPosition(new PixelPoint(90, 40));

        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(0, 0, 99, 49)));
    }

    [Test]
    public void UpdateCursorPosition_WhenAspectLocked_DoesNotApplySizeSnap()
    {
        var stateMachine = new SelectionStateMachine(
            quickCrop: false,
            snapSizes: [new CaptureSnapSize(100, 50)],
            snapDistance: 30);

        stateMachine.BeginDrag(new PixelPoint(0, 0));
        stateMachine.SetModifiers(SelectionModifier.LockAspectRatio);
        stateMachine.UpdateCursorPosition(new PixelPoint(90, 40));

        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(0, 0, 90, 90)));
    }

    [Test]
    public void UpdateCursorPosition_WhenCtrlPressedDuringCreate_MovesSelectionWithoutResizing()
    {
        var stateMachine = new SelectionStateMachine(quickCrop: false);
        stateMachine.BeginDrag(new PixelPoint(10, 20));
        stateMachine.UpdateCursorPosition(new PixelPoint(50, 80));

        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(10, 20, 40, 60)));

        stateMachine.SetModifiers(SelectionModifier.PixelNudge);
        stateMachine.UpdateCursorPosition(new PixelPoint(70, 90));

        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(30, 30, 40, 60)));

        stateMachine.SetModifiers(SelectionModifier.None);
        stateMachine.UpdateCursorPosition(new PixelPoint(80, 100));

        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(30, 30, 50, 70)));
    }

    [Test]
    public void UpdateCursorPosition_WhenCtrlHeldAtCreateStart_ContinuesResizing()
    {
        var stateMachine = new SelectionStateMachine(quickCrop: false);
        stateMachine.SetModifiers(SelectionModifier.PixelNudge);
        stateMachine.BeginDrag(new PixelPoint(10, 10));
        stateMachine.UpdateCursorPosition(new PixelPoint(40, 50));

        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(10, 10, 30, 40)));
    }

    [Test]
    public void BeginResize_UpdatesOppositeEdge()
    {
        var stateMachine = new SelectionStateMachine(quickCrop: false);
        stateMachine.BeginDrag(new PixelPoint(10, 10));
        stateMachine.UpdateCursorPosition(new PixelPoint(50, 40));
        stateMachine.EndDrag();

        stateMachine.BeginResize(SelectionHandle.BottomRight, new PixelPoint(50, 40));
        stateMachine.UpdateCursorPosition(new PixelPoint(80, 70));
        stateMachine.EndDrag();

        Assert.That(stateMachine.CurrentState, Is.EqualTo(CaptureState.Selected));
        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(10, 10, 70, 60)));
    }

    [Test]
    public void ActiveMonitorBounds_ClipDragAndKeyboardResize()
    {
        var bounds = new PixelRect(-800, 100, 800, 600);
        var stateMachine = new SelectionStateMachine(quickCrop: false, snapSizes: [], selectionBounds: bounds);
        stateMachine.BeginDrag(new PixelPoint(-300, 200));
        stateMachine.UpdateCursorPosition(new PixelPoint(200, 850));
        stateMachine.EndDrag();

        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(-300, 200, 300, 500)));
        stateMachine.ResizeSelection(500, 500);
        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(-300, 200, 300, 500)));

        RegionSelectionResult? result = null;
        stateMachine.SelectionConfirmed += value => result = value;
        Assert.That(stateMachine.TryConfirm(), Is.True);
        Assert.That(result!.Value.Region.Right, Is.EqualTo(bounds.Right));
        Assert.That(result.Value.Region.Bottom, Is.EqualTo(bounds.Bottom));
    }

    [Test]
    public void ActiveMonitorBounds_MouseMoveStopsAtEdgeWithoutShrinking()
    {
        var stateMachine = CreateBoundedSelection();

        stateMachine.BeginMove(new PixelPoint(800, 200));
        stateMachine.UpdateCursorPosition(new PixelPoint(950, 200));
        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(800, 100, 200, 200)));

        // Moving further than the selection width used to leave an empty selection.
        stateMachine.UpdateCursorPosition(new PixelPoint(1100, 900));
        stateMachine.EndDrag();

        Assert.That(stateMachine.CurrentState, Is.EqualTo(CaptureState.Selected));
        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(800, 600, 200, 200)));
    }

    [Test]
    public void ActiveMonitorBounds_ArrowNudgeStopsAtEdgeWithoutShrinking()
    {
        var stateMachine = CreateBoundedSelection();

        for (int i = 0; i < 105; i++)
            stateMachine.NudgeSelection(1, 0);
        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(800, 100, 200, 200)));

        for (int i = 0; i < 105; i++)
            stateMachine.NudgeSelection(-1, 0);
        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(695, 100, 200, 200)));
    }

    [Test]
    public void ActiveMonitorBounds_CtrlMoveDuringCreationStopsAtEdgeWithoutShrinking()
    {
        var stateMachine = new SelectionStateMachine(quickCrop: false, snapSizes: [], selectionBounds: BoundedMonitor);
        stateMachine.BeginDrag(new PixelPoint(700, 100));
        stateMachine.UpdateCursorPosition(new PixelPoint(900, 300));

        stateMachine.SetModifiers(SelectionModifier.PixelNudge);
        stateMachine.UpdateCursorPosition(new PixelPoint(1300, 300));
        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(800, 100, 200, 200)));

        stateMachine.SetModifiers(SelectionModifier.None);
        stateMachine.EndDrag();
        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(800, 100, 200, 200)));
    }

    private static readonly PixelRect BoundedMonitor = new(0, 0, 1000, 800);

    private static SelectionStateMachine CreateBoundedSelection()
    {
        var stateMachine = new SelectionStateMachine(quickCrop: false, snapSizes: [], selectionBounds: BoundedMonitor);
        stateMachine.BeginDrag(new PixelPoint(700, 100));
        stateMachine.UpdateCursorPosition(new PixelPoint(900, 300));
        stateMachine.EndDrag();
        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(700, 100, 200, 200)));
        return stateMachine;
    }

    [Test]
    public void DeferredQuickCapture_LeavesSelectionEditableUntilConfirmed()
    {
        var stateMachine = new SelectionStateMachine(quickCrop: true, snapSizes: []);
        stateMachine.BeginDrag(new PixelPoint(10, 20));
        stateMachine.UpdateCursorPosition(new PixelPoint(110, 120));
        stateMachine.EndDrag(deferConfirmation: true);

        Assert.That(stateMachine.CurrentState, Is.EqualTo(CaptureState.Selected));
        stateMachine.BeginMove(new PixelPoint(50, 50));
        stateMachine.UpdateCursorPosition(new PixelPoint(60, 60));
        stateMachine.EndDrag(deferConfirmation: true);

        Assert.That(stateMachine.SelectionRect, Is.EqualTo(new PixelRect(20, 30, 100, 100)));
        Assert.That(stateMachine.TryConfirm(), Is.True);
    }

    [Test]
    public void HitTest_ReturnsHandleForCornerAndBody()
    {
        var selection = new PixelRect(10, 20, 100, 80);

        Assert.That(SelectionSnapHelper.HitTest(selection, new PixelPoint(10, 20), 8), Is.EqualTo(SelectionHandle.TopLeft));
        Assert.That(SelectionSnapHelper.HitTest(selection, new PixelPoint(110, 100), 8), Is.EqualTo(SelectionHandle.BottomRight));
        Assert.That(SelectionSnapHelper.HitTest(selection, new PixelPoint(60, 60), 8), Is.EqualTo(SelectionHandle.Body));
        Assert.That(SelectionSnapHelper.HitTest(selection, new PixelPoint(200, 200), 8), Is.EqualTo(SelectionHandle.None));
    }
}
