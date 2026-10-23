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

using XerahS.Common;
using XerahS.Core;

namespace XerahS.UI.Helpers;

/// <summary>A tool of ShareX's Tools menu. <see cref="Id"/> is its navigation tag, which also names it in
/// <see cref="ApplicationConfig.HiddenTools"/>; <see cref="Tags"/> are the main window's menu entries it covers.</summary>
internal sealed record ToolMenuItem(string Id, string Header, WorkflowType Job, params string[] Tags)
{
    public bool IsAvailable => WorkflowCatalog.IsAvailable(Job);
}

internal sealed record ToolMenuCategory(string Header, IReadOnlyList<ToolMenuItem> Tools);

/// <summary>
/// ShareX's tool categories (MainMenuBuilder.BuildToolCategories), shared by the tray menu, the Tools page,
/// and the main window's menus, and the tools the user hid from them (ShareX's HiddenTools).
/// </summary>
internal static class ToolMenuCatalog
{
    /// <summary>Raised on the UI thread when a tool is hidden or shown again.</summary>
    public static event EventHandler? HiddenToolsChanged;

    // As in ShareX, the tools of a category are in alphabetical order. ShareX's "Remote storage browser"
    // is XerahS's Media browser.
    public static IReadOnlyList<ToolMenuCategory> Categories { get; } =
    [
        new("Screen & color",
        [
            new("Tools_ColorPicker", "Color picker...", WorkflowType.ColorPicker),
            new("Tools_MouseHighlighter", "Mouse highlighter...", WorkflowType.MouseHighlighter),
            new("Tools_PinToScreen", "Pin to screen...", WorkflowType.PinToScreen,
                "Tools_PinToScreenFromScreen", "Tools_PinToScreenFromClipboard", "Tools_PinToScreenFromFile", "Tools_PinToScreenCloseAll"),
            new("Tools_Ruler", "Ruler...", WorkflowType.Ruler),
            new("Tools_ScreenColorPicker", "Screen color picker...", WorkflowType.ScreenColorPicker)
        ]),
        new("Images",
        [
            new("Tools_AnimatedGifMaker", "Animated GIF maker...", WorkflowType.AnimatedGifMaker),
            new("Tools_BackgroundRemover", "Background remover...", WorkflowType.BackgroundRemover),
            new("Tools_IconConverter", "Icon converter...", WorkflowType.IconConverter),
            new("Tools_ImageBeautifier", "Image beautifier...", WorkflowType.ImageBeautifier),
            new("Tools_ImageCombiner", "Image combiner...", WorkflowType.ImageCombiner),
            new("Tools_ImageComparer", "Image comparer...", WorkflowType.ImageComparer),
            new("Tools_ImageConverter", "Image converter...", WorkflowType.ImageConverter),
            new("Tools_ImageEditor", "Image editor...", WorkflowType.ImageEditor),
            new("Tools_ImageEffects", "Image effects...", WorkflowType.ImageEffects),
            new("Tools_ImageResizer", "Image resizer...", WorkflowType.ImageResizer),
            new("Tools_ImageSplitter", "Image splitter...", WorkflowType.ImageSplitter),
            new("Tools_ImageThumbnailer", "Image thumbnailer...", WorkflowType.ImageThumbnailer),
            new("Tools_ImageViewer", "Image viewer...", WorkflowType.ImageViewer),
            new("Tools_ImageWatermark", "Image watermark...", WorkflowType.ImageWatermark)
        ]),
        new("Video",
        [
            new("Tools_VideoConverter", "Video converter...", WorkflowType.VideoConverter),
            new("Tools_VideoThumbnailer", "Video thumbnailer...", WorkflowType.VideoThumbnailer),
            new("Tools_VideoTrimmer", "Video trimmer...", WorkflowType.VideoTrimmer)
        ]),
        new("Data & text",
        [
            new("Tools_AnalyzeImage", "Analyze image...", WorkflowType.AnalyzeImage),
            new("Tools_HashCheck", "Hash checker...", WorkflowType.HashCheck),
            new("Tools_IndexFolder", "Index folder...", WorkflowType.IndexFolder),
            new("Tools_MediaBrowser", "Media browser...", WorkflowType.MediaBrowser),
            new("Tools_Metadata", "Metadata...", WorkflowType.Metadata),
            new("Tools_OCR", "OCR...", WorkflowType.OCR),
            new("Tools_QRCode", "QR code...", WorkflowType.QRCode, "Tools_QrGenerator", "Tools_QrScanScreen", "Tools_QrScanRegion")
        ]),
        new("System",
        [
            new("Tools_BorderlessWindow", "Borderless window...", WorkflowType.BorderlessWindow),
            new("Tools_ClipboardViewer", "Clipboard viewer...", WorkflowType.ClipboardViewer),
            new("Tools_InspectWindow", "Inspect window...", WorkflowType.InspectWindow),
            new("Tools_MonitorTest", "Monitor test...", WorkflowType.MonitorTest),
            new("Tools_NetworkMonitor", "Network monitor...", WorkflowType.NetworkMonitor)
        ])
    ];

    /// <summary>The navigation tag that opens a tool; a tool that covers several entries opens with its first.</summary>
    public static string GetLaunchTag(ToolMenuItem tool) => tool.Id switch
    {
        "Tools_QRCode" => "Tools_QrGenerator",
        _ => tool.Id
    };

    public static bool IsHidden(ToolMenuItem tool) =>
        SettingsManager.Settings.HiddenTools?.Any(id => string.Equals(id, tool.Id, StringComparison.Ordinal)) == true;

    /// <summary>Whether a main window menu entry belongs to a hidden tool. Entries of no tool are never hidden.</summary>
    public static bool IsTagHidden(string tag)
    {
        foreach (ToolMenuCategory category in Categories)
        {
            foreach (ToolMenuItem tool in category.Tools)
            {
                if (string.Equals(tool.Id, tag, StringComparison.Ordinal) || tool.Tags.Contains(tag, StringComparer.Ordinal))
                {
                    return IsHidden(tool);
                }
            }
        }

        return false;
    }

    /// <summary>ShareX's SetToolVisibility: updates the hidden tools and saves the application settings.</summary>
    public static void SetHidden(ToolMenuItem tool, bool hidden)
    {
        List<string> hiddenTools = SettingsManager.Settings.HiddenTools ??= new List<string>();
        bool changed = hidden
            ? !hiddenTools.Contains(tool.Id, StringComparer.Ordinal)
            : hiddenTools.RemoveAll(id => string.Equals(id, tool.Id, StringComparison.Ordinal)) > 0;

        if (!changed)
        {
            return;
        }

        if (hidden)
        {
            hiddenTools.Add(tool.Id);
        }

        HiddenToolsChanged?.Invoke(null, EventArgs.Empty);
        _ = SaveAsync();
    }

    private static async Task SaveAsync()
    {
        try
        {
            await SettingsManager.SaveApplicationConfigAsync();
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Failed to save the hidden tools");
        }
    }

    /// <summary>The tools a menu shows, by category: available, and not hidden unless <paramref name="includeHidden"/>.</summary>
    public static IEnumerable<(ToolMenuCategory Category, IReadOnlyList<ToolMenuItem> Tools)> GetVisibleCategories(bool includeHidden = false)
    {
        foreach (ToolMenuCategory category in Categories)
        {
            ToolMenuItem[] tools = category.Tools.Where(tool => tool.IsAvailable && (includeHidden || !IsHidden(tool))).ToArray();
            if (tools.Length > 0)
            {
                yield return (category, tools);
            }
        }
    }
}
