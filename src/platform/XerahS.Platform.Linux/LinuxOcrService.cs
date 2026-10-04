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

using System.Globalization;
using System.Runtime.InteropServices;
using SkiaSharp;
using XerahS.Common;
using XerahS.Platform.Abstractions;

namespace XerahS.Platform.Linux;

/// <summary>
/// OCR with the system's Tesseract library (libtesseract), through its C API.
/// ShareX uses the Windows OCR engine; Tesseract is the Linux equivalent.
/// </summary>
public class LinuxOcrService : IOcrService
{
    internal const string InstallHint =
        "Install Tesseract to use OCR: \"sudo dnf install tesseract-libs tesseract-langpack-eng\" on Fedora, " +
        "or \"sudo apt install libtesseract5 tesseract-ocr-eng\" on Debian and Ubuntu.";

    private const int PageSegModeAuto = 3;
    private const int PageSegModeSingleLine = 7;
    private const int MaxImageDimension = 32767;

    private static readonly string[] LibraryNames = ["libtesseract.so.5", "libtesseract.so.5.5", "libtesseract.so"];
    private static readonly HashSet<string> NonLanguageData = new(StringComparer.Ordinal) { "osd", "equ" };
    private static readonly Lazy<TesseractLibrary?> Library = new(TesseractLibrary.TryLoad);

    private readonly Func<IReadOnlyList<string>> _tessdataDirectories;

    public LinuxOcrService() : this(GetTessdataDirectories) { }

    internal LinuxOcrService(Func<IReadOnlyList<string>> tessdataDirectories)
    {
        _tessdataDirectories = tessdataDirectories;
    }

    public bool IsSupported => Library.Value != null;

    public string? UnavailableMessage => IsSupported ? null : "The Tesseract OCR library was not found. " + InstallHint;

    public OcrLanguage[] GetAvailableLanguages()
    {
        return FindLanguages()
            .Select(code => new OcrLanguage(GetDisplayName(code), ToLanguageTag(code)))
            .DistinctBy(language => language.LanguageTag, StringComparer.OrdinalIgnoreCase)
            .OrderBy(language => language.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<OcrResult> RecognizeAsync(SKBitmap image, OcrOptions options)
    {
        try
        {
            var library = Library.Value ?? throw new InvalidOperationException(UnavailableMessage);
            var installed = FindLanguagesWithDirectory();
            string code = ToTesseractCode(options.Language, installed.Keys);
            if (!installed.TryGetValue(code, out string? directory))
            {
                throw new InvalidOperationException(
                    $"The OCR language data for \"{options.Language}\" ({code}) is not installed. " +
                    $"Install \"tesseract-langpack-{code}\" on Fedora, or \"tesseract-ocr-{code.Replace('_', '-')}\" on Debian and Ubuntu.");
            }

            string text = await Task.Run(() =>
            {
                using var prepared = PrepareImage(image, options.ScaleFactor);
                return library.Recognize(prepared, directory, code, options.SingleLine ? PageSegModeSingleLine : PageSegModeAuto);
            });

            return new OcrResult { Text = NormalizeText(text, options.SingleLine), Success = true };
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Tesseract OCR");
            return new OcrResult { Text = string.Empty, Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>Scales the image like the Windows OCR service and flattens it on white as opaque RGBA.</summary>
    internal static SKBitmap PrepareImage(SKBitmap source, float requestedScaleFactor)
    {
        float scale = float.IsFinite(requestedScaleFactor) ? Math.Max(requestedScaleFactor, 1f) : 1f;
        int largest = Math.Max(source.Width, source.Height);
        if (largest > 0) scale = Math.Min(scale, (float)MaxImageDimension / largest);

        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var target = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.White);
        using var sourceImage = SKImage.FromBitmap(source);
        canvas.DrawImage(sourceImage, new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        canvas.Flush();
        return target;
    }

    private static string NormalizeText(string text, bool singleLine)
    {
        // Tesseract ends each paragraph with a blank line and adds a form feed after the page.
        var lines = text.Replace("\f", string.Empty).Replace("\r\n", "\n").Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => line.Length > 0);
        return string.Join(singleLine ? " " : Environment.NewLine, lines);
    }

    private IEnumerable<string> FindLanguages() => FindLanguagesWithDirectory().Keys;

    /// <summary>Installed language codes (for example "eng" or "script/Latin") and the tessdata folder of each.</summary>
    private Dictionary<string, string> FindLanguagesWithDirectory()
    {
        var languages = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string directory in _tessdataDirectories())
        {
            if (!Directory.Exists(directory)) continue;
            foreach (string file in Directory.EnumerateFiles(directory, "*.traineddata", SearchOption.AllDirectories))
            {
                string code = Path.ChangeExtension(Path.GetRelativePath(directory, file), null).Replace(Path.DirectorySeparatorChar, '/');
                if (!NonLanguageData.Contains(code)) languages.TryAdd(code, directory);
            }
        }
        return languages;
    }

    internal static IReadOnlyList<string> GetTessdataDirectories()
    {
        var directories = new List<string>();
        string? prefix = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            directories.Add(prefix);
            directories.Add(Path.Combine(prefix, "tessdata"));
        }

        directories.AddRange([
            "/usr/share/tesseract/tessdata",          // Fedora
            "/usr/share/tessdata",                    // Arch
            "/usr/share/tesseract-ocr/5/tessdata",    // Debian, Ubuntu
            "/usr/share/tesseract-ocr/4.00/tessdata",
            "/usr/local/share/tessdata"
        ]);
        return directories;
    }

    /// <summary>Maps a stored language tag ("en", "zh-Hans") to a Tesseract code ("eng", "chi_sim").</summary>
    internal static string ToTesseractCode(string? languageTag, IEnumerable<string> installed)
    {
        string tag = string.IsNullOrWhiteSpace(languageTag) ? "en" : languageTag.Trim();
        if (installed.Contains(tag, StringComparer.Ordinal)) return tag;

        string lower = tag.ToLowerInvariant();
        if (lower is "zh-hans" or "zh-cn" or "zh-sg" or "zh") return "chi_sim";
        if (lower is "zh-hant" or "zh-tw" or "zh-hk" or "zh-mo") return "chi_tra";
        if (lower is "nb" or "nn" or "no" || lower.StartsWith("nb-") || lower.StartsWith("nn-")) return "nor";
        if (lower.StartsWith("sr-latn")) return "srp_latn";

        try
        {
            var culture = CultureInfo.GetCultureInfo(tag);
            string code = culture.ThreeLetterISOLanguageName;
            if (!string.IsNullOrEmpty(code) && code != "ivl") return code;
        }
        catch (CultureNotFoundException)
        {
        }

        return tag;
    }

    /// <summary>Maps a Tesseract code back to the tag XerahS stores, so saved settings work on every platform.</summary>
    internal static string ToLanguageTag(string code)
    {
        switch (code)
        {
            case "chi_sim": return "zh-Hans";
            case "chi_tra": return "zh-Hant";
            case "nor": return "nb";
            case "srp_latn": return "sr-Latn";
        }

        if (code.Length == 3 && FindCulture(code) is { } culture) return culture.Name;
        return code;
    }

    internal static string GetDisplayName(string code)
    {
        if (code.StartsWith("script/", StringComparison.Ordinal)) return code["script/".Length..] + " (script)";

        bool vertical = code.EndsWith("_vert", StringComparison.Ordinal);
        string baseCode = vertical ? code[..^"_vert".Length] : code;
        string tag = ToLanguageTag(baseCode);
        string name = tag == baseCode ? baseCode : CultureInfo.GetCultureInfo(tag).DisplayName;
        return vertical ? name + " (vertical)" : name;
    }

    private static CultureInfo? FindCulture(string threeLetterCode) =>
        CultureInfo.GetCultures(CultureTypes.NeutralCultures)
            .FirstOrDefault(culture => culture.ThreeLetterISOLanguageName == threeLetterCode && culture.Name.Length > 0);

    private sealed unsafe class TesseractLibrary
    {
        private readonly delegate* unmanaged[Cdecl]<IntPtr> _create;
        private readonly delegate* unmanaged[Cdecl]<IntPtr, void> _delete;
        private readonly delegate* unmanaged[Cdecl]<IntPtr, byte*, byte*, int> _init3;
        private readonly delegate* unmanaged[Cdecl]<IntPtr, int, void> _setPageSegMode;
        private readonly delegate* unmanaged[Cdecl]<IntPtr, byte*, int, int, int, int, void> _setImage;
        private readonly delegate* unmanaged[Cdecl]<IntPtr, int, void> _setSourceResolution;
        private readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr> _getUtf8Text;
        private readonly delegate* unmanaged[Cdecl]<IntPtr, void> _deleteText;
        private readonly delegate* unmanaged[Cdecl]<IntPtr, void> _end;

        private TesseractLibrary(IntPtr handle)
        {
            _create = (delegate* unmanaged[Cdecl]<IntPtr>)NativeLibrary.GetExport(handle, "TessBaseAPICreate");
            _delete = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(handle, "TessBaseAPIDelete");
            _init3 = (delegate* unmanaged[Cdecl]<IntPtr, byte*, byte*, int>)NativeLibrary.GetExport(handle, "TessBaseAPIInit3");
            _setPageSegMode = (delegate* unmanaged[Cdecl]<IntPtr, int, void>)NativeLibrary.GetExport(handle, "TessBaseAPISetPageSegMode");
            _setImage = (delegate* unmanaged[Cdecl]<IntPtr, byte*, int, int, int, int, void>)NativeLibrary.GetExport(handle, "TessBaseAPISetImage");
            _setSourceResolution = (delegate* unmanaged[Cdecl]<IntPtr, int, void>)NativeLibrary.GetExport(handle, "TessBaseAPISetSourceResolution");
            _getUtf8Text = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr>)NativeLibrary.GetExport(handle, "TessBaseAPIGetUTF8Text");
            _deleteText = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(handle, "TessDeleteText");
            _end = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(handle, "TessBaseAPIEnd");
        }

        public static TesseractLibrary? TryLoad()
        {
            if (!OperatingSystem.IsLinux()) return null;
            foreach (string name in LibraryNames.Concat(FindLibraryFiles()))
            {
                if (!NativeLibrary.TryLoad(name, out IntPtr handle)) continue;
                try
                {
                    return new TesseractLibrary(handle);
                }
                catch (EntryPointNotFoundException ex)
                {
                    DebugHelper.WriteException(ex, $"Tesseract library {name} is missing a C API function");
                }
            }

            DebugHelper.WriteLine("Tesseract OCR library not found.");
            return null;
        }

        /// <summary>
        /// Tesseract 5.4 and later include the minor version in the library name on some distributions
        /// (Fedora has libtesseract.so.5.5), so also look in the standard library folders.
        /// </summary>
        private static IEnumerable<string> FindLibraryFiles()
        {
            string[] folders = ["/usr/lib64", "/usr/lib", "/usr/lib/x86_64-linux-gnu", "/usr/lib/aarch64-linux-gnu", "/usr/local/lib"];
            return folders.Where(Directory.Exists)
                .SelectMany(folder => Directory.EnumerateFiles(folder, "libtesseract.so.*"))
                .OrderByDescending(path => path.Length)
                .ThenByDescending(path => path, StringComparer.Ordinal);
        }

        /// <summary>Runs one recognition. Each call uses its own Tesseract instance, so calls can run in parallel.</summary>
        public string Recognize(SKBitmap rgba, string tessdataDirectory, string language, int pageSegMode)
        {
            IntPtr api = _create();
            if (api == IntPtr.Zero) throw new InvalidOperationException("Tesseract could not be started.");
            try
            {
                byte[] directory = NullTerminated(tessdataDirectory);
                byte[] languageBytes = NullTerminated(language);
                fixed (byte* directoryPointer = directory)
                fixed (byte* languagePointer = languageBytes)
                {
                    if (_init3(api, directoryPointer, languagePointer) != 0)
                        throw new InvalidOperationException($"Tesseract could not load the \"{language}\" OCR language data from {tessdataDirectory}.");
                }

                _setPageSegMode(api, pageSegMode);
                _setImage(api, (byte*)rgba.GetPixels(), rgba.Width, rgba.Height, 4, rgba.RowBytes);
                // Captures have no DPI; without a resolution Tesseract warns and guesses one.
                _setSourceResolution(api, 300);

                IntPtr text = _getUtf8Text(api);
                if (text == IntPtr.Zero) return string.Empty;
                try
                {
                    return Marshal.PtrToStringUTF8(text) ?? string.Empty;
                }
                finally
                {
                    _deleteText(text);
                }
            }
            finally
            {
                _end(api);
                _delete(api);
            }
        }

        private static byte[] NullTerminated(string value)
        {
            int length = System.Text.Encoding.UTF8.GetByteCount(value);
            byte[] bytes = new byte[length + 1];
            System.Text.Encoding.UTF8.GetBytes(value, bytes);
            return bytes;
        }
    }
}
