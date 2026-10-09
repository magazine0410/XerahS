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
using XerahS.Core;
using XerahS.UI.ViewModels;

namespace XerahS.Tests.ViewModels;

/// <summary>ShareX's Upload settings page: the simultaneous upload limit, the buffer size, and the retry count.</summary>
[TestFixture]
[NonParallelizable]
public sealed class SettingsViewModelUploadTests
{
    private (int Limit, int Power, int Retry, bool BinaryUnits) _previous;

    [SetUp]
    public void SetUp()
    {
        var settings = SettingsManager.Settings;
        _previous = (settings.UploadLimit, settings.BufferSizePower, settings.MaxUploadFailRetry, settings.BinaryUnits);
    }

    [TearDown]
    public void TearDown()
    {
        var settings = SettingsManager.Settings;
        (settings.UploadLimit, settings.BufferSizePower, settings.MaxUploadFailRetry, settings.BinaryUnits) = _previous;
    }

    [TestCase(false, "1 KB", "33 KB", "8 MB")]
    [TestCase(true, "1 KiB", "32 KiB", "8 MiB")]
    public void BufferSizes_AreShareXsList_InTheChosenUnits(bool binaryUnits, string first, string defaultSize, string last)
    {
        SettingsManager.Settings.BinaryUnits = binaryUnits;
        SettingsManager.Settings.BufferSizePower = 5;
        var viewModel = new SettingsViewModel();

        Assert.That(viewModel.BufferSizeOptions, Has.Length.EqualTo(14));
        Assert.That(viewModel.BufferSizeOptions[0], Is.EqualTo(first));
        Assert.That(viewModel.BufferSizeOptions[viewModel.BufferSizePower], Is.EqualTo(defaultSize));
        Assert.That(viewModel.BufferSizeOptions[^1], Is.EqualTo(last));
    }

    [Test]
    public void UploadSettings_StayWithinShareXsRanges()
    {
        var viewModel = new SettingsViewModel
        {
            UploadLimit = 40,
            MaxUploadFailRetry = 9,
            BufferSizePower = 20
        };

        Assert.That(SettingsManager.Settings.UploadLimit, Is.EqualTo(25));
        Assert.That(SettingsManager.Settings.MaxUploadFailRetry, Is.EqualTo(5));
        Assert.That(SettingsManager.Settings.BufferSizePower, Is.EqualTo(13));

        viewModel.BufferSizePower = -1; // No selection in the list keeps the setting.
        Assert.That(SettingsManager.Settings.BufferSizePower, Is.EqualTo(13));
        viewModel.UploadLimit = 0;
        Assert.That(SettingsManager.Settings.UploadLimit, Is.Zero, "0 disables the limit.");
    }
}
