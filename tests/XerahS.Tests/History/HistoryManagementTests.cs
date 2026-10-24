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
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using XerahS.Core;
using XerahS.History;
using XerahS.Services.Abstractions;
using XerahS.Tests.Xip0052;
using XerahS.UI.ViewModels;
using XerahS.UI.Views;

namespace XerahS.Tests.History;

/// <summary>
/// The history management actions ported from ShareX's history windows: edit tag, edit item, rename file,
/// delete several entries (with or without their files), import a folder, statistics, and the URL filter.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class HistoryManagementTests
{
    private string _directory = null!;
    private string _originalFolder = null!;
    private HistorySettings _originalHistorySettings = null!;
    private ImageHistorySettings _originalImageSettings = null!;
    private RecordingDialogService _dialogs = null!;

    [SetUp]
    public void SetUp()
    {
        _originalFolder = SettingsManager.PersonalFolder;
        _originalHistorySettings = SettingsManager.Settings.HistorySettings;
        _originalImageSettings = SettingsManager.Settings.ImageHistorySettings;
        _directory = Path.Combine(Path.GetTempPath(), "xerahs-history-management-" + Guid.NewGuid().ToString("N"));
        SettingsManager.PersonalFolder = _directory;
        SettingsManager.Settings.HistorySettings = new HistorySettings();
        SettingsManager.Settings.ImageHistorySettings = new ImageHistorySettings();
        Directory.CreateDirectory(SettingsManager.HistoryFolder);
        _dialogs = new RecordingDialogService();
    }

    [TearDown]
    public void TearDown()
    {
        SettingsManager.Settings.HistorySettings = _originalHistorySettings;
        SettingsManager.Settings.ImageHistorySettings = _originalImageSettings;
        SettingsManager.PersonalFolder = _originalFolder;
        Directory.Delete(_directory, true);
    }

    [Test]
    public async Task EditTag_SavesTheTag_AndAnEmptyTagRemovesIt()
    {
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        var entry = Item("tagged.png", 0);
        Assert.That(manager.AppendHistoryItem(entry), Is.True);
        using var vm = ViewModel();
        await vm.LoadHistoryCommand.ExecuteAsync(null);

        _dialogs.InputAnswers.Enqueue("needs review");
        await vm.EditTagCommand.ExecuteAsync(vm.HistoryItems[0]);
        Assert.Multiple(() =>
        {
            Assert.That(_dialogs.LastInputTitle, Is.EqualTo("Edit tag"));
            Assert.That(_dialogs.LastInputDefault, Is.Empty);
            Assert.That(manager.GetHistoryItem(entry.Id)!.Tag, Is.EqualTo("needs review"));
            Assert.That(vm.HistoryItems[0].Tag, Is.EqualTo("needs review"));
        });

        _dialogs.InputAnswers.Enqueue(null);
        await vm.EditTagCommand.ExecuteAsync(vm.HistoryItems[0]);
        Assert.That(manager.GetHistoryItem(entry.Id)!.Tag, Is.EqualTo("needs review"), "Cancel keeps the tag.");

        _dialogs.InputAnswers.Enqueue(string.Empty);
        await vm.EditTagCommand.ExecuteAsync(vm.HistoryItems[0]);
        Assert.Multiple(() =>
        {
            Assert.That(_dialogs.LastInputDefault, Is.EqualTo("needs review"));
            Assert.That(manager.GetHistoryItem(entry.Id)!.Tags.ContainsKey("Tag"), Is.False, "As in ShareX, an empty tag removes the Tag entry.");
        });
    }

    [Test]
    public async Task EditItem_SavesEveryField_AndParsesTagsOneKeyValuePerLine()
    {
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        var entry = Item("before.png", 0);
        entry.Tags["WindowTitle"] = "Before";
        entry.Favorite = true;
        Assert.That(manager.AppendHistoryItem(entry), Is.True);
        using var vm = ViewModel();
        await vm.LoadHistoryCommand.ExecuteAsync(null);
        vm.SetSelectedHistoryItems([vm.HistoryItems[0]]);

        HistoryItemEditViewModel? shown = null;
        string? shownTags = null;
        vm.ShowEditItemDialogAsync = dialog =>
        {
            shown = dialog;
            shownTags = dialog.Tags;
            dialog.FileName = "after.png";
            dialog.FilePath = Path.Combine(_directory, "after.png");
            dialog.Type = "File";
            dialog.Host = "other.example.test";
            dialog.Url = "https://other.example.test/after.png";
            dialog.ThumbnailUrl = "https://other.example.test/after_thumb.png";
            dialog.DeletionUrl = "https://other.example.test/delete";
            dialog.ShortenedUrl = "https://o.example.test/a";
            dialog.Tags = "WindowTitle=After\nFavorite\n  ProcessName = app.exe\n\n=ignored";
            return Task.FromResult(true);
        };
        await vm.EditItemCommand.ExecuteAsync(vm.HistoryItems[0]);

        HistoryItem saved = manager.GetHistoryItem(entry.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(shown, Is.Not.Null);
            Assert.That(shown!.Date, Is.EqualTo(entry.DateTime.ToString("G")), "The date is shown, read-only.");
            Assert.That(shownTags, Is.EqualTo("Favorite=" + Environment.NewLine + "WindowTitle=Before"), "Tags are listed one key=value per line, by key.");
            Assert.That(saved.FileName, Is.EqualTo("after.png"));
            Assert.That(saved.FilePath, Is.EqualTo(Path.Combine(_directory, "after.png")));
            Assert.That(saved.DateTime, Is.EqualTo(entry.DateTime));
            Assert.That(saved.Type, Is.EqualTo("File"));
            Assert.That(saved.Host, Is.EqualTo("other.example.test"));
            Assert.That(saved.URL, Is.EqualTo("https://other.example.test/after.png"));
            Assert.That(saved.ThumbnailURL, Is.EqualTo("https://other.example.test/after_thumb.png"));
            Assert.That(saved.DeletionURL, Is.EqualTo("https://other.example.test/delete"));
            Assert.That(saved.ShortenedURL, Is.EqualTo("https://o.example.test/a"));
            Assert.That(saved.Tags, Is.EqualTo(new Dictionary<string, string?>
            {
                ["WindowTitle"] = "After",
                ["Favorite"] = null,
                ["ProcessName"] = " app.exe"
            }));
            Assert.That(vm.HistoryItems[0].FileName, Is.EqualTo("after.png"), "The row shows the new values.");
            Assert.That(vm.SelectedHistoryItems, Has.Count.EqualTo(1), "The selection is kept.");
        });

        vm.ShowEditItemDialogAsync = dialog => { dialog.FileName = "cancelled.png"; return Task.FromResult(false); };
        await vm.EditItemCommand.ExecuteAsync(vm.HistoryItems[0]);
        Assert.That(manager.GetHistoryItem(entry.Id)!.FileName, Is.EqualTo("after.png"), "Cancel keeps the entry.");
    }

    [Test]
    public async Task RenameFile_RenamesTheFileKeepingItsExtension_WithItsSidecar_AndUpdatesTheEntry()
    {
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        var entry = Item("shot.png", 0);
        File.WriteAllBytes(entry.FilePath, [1, 2, 3]);
        File.WriteAllText(Path.Combine(_directory, "shot.xann"), "{}");
        Assert.That(manager.AppendHistoryItem(entry), Is.True);
        using var vm = ViewModel();
        await vm.LoadHistoryCommand.ExecuteAsync(null);

        _dialogs.InputAnswers.Enqueue("renamed.jpg");
        await vm.RenameFileCommand.ExecuteAsync(vm.HistoryItems[0]);
        HistoryItem saved = manager.GetHistoryItem(entry.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(_dialogs.LastInputTitle, Is.EqualTo("Rename file"));
            Assert.That(_dialogs.LastInputDefault, Is.EqualTo("shot"), "The prompt shows the name without its extension.");
            Assert.That(File.Exists(Path.Combine(_directory, "renamed.jpg.png")), Is.True, "The extension is kept, as in ShareX.");
            Assert.That(File.Exists(Path.Combine(_directory, "renamed.jpg.xann")), Is.True, "The annotation sidecar follows the file.");
            Assert.That(File.Exists(entry.FilePath), Is.False);
            Assert.That(saved.FileName, Is.EqualTo("renamed.jpg.png"));
            Assert.That(saved.FilePath, Is.EqualTo(Path.Combine(_directory, "renamed.jpg.png")));
            Assert.That(vm.HistoryItems[0].FileName, Is.EqualTo("renamed.jpg.png"));
        });

        _dialogs.InputAnswers.Enqueue("RENAMED.JPG");
        await vm.RenameFileCommand.ExecuteAsync(vm.HistoryItems[0]);
        Assert.That(manager.GetHistoryItem(entry.Id)!.FileName, Is.EqualTo("renamed.jpg.png"), "A name that differs only in case is ignored, as in ShareX.");

        var missing = Item("gone.png", 1);
        Assert.That(manager.AppendHistoryItem(missing), Is.True);
        await vm.LoadHistoryCommand.ExecuteAsync(null);
        HistoryItem missingRow = vm.HistoryItems.Single(item => item.FileName == "gone.png");
        _dialogs.InputAnswers.Enqueue("still gone");
        await vm.RenameFileCommand.ExecuteAsync(missingRow);
        saved = manager.GetHistoryItem(missing.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(saved.FileName, Is.EqualTo("still gone.png"), "As in ShareX, a missing file only renames the entry.");
            Assert.That(saved.FilePath, Is.EqualTo(missing.FilePath));
        });
    }

    [Test]
    public async Task EditItem_SavedUnchanged_KeepsTagsWithLineBreaks()
    {
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        var entry = Item("ocr.png", 0);
        string other = Path.Combine(_directory, "notes.txt");
        entry.Tags["OcrText"] = "First line\nAnnotationSidecarPath=" + other + "\nFavorite";
        Dictionary<string, string?> expected = new(entry.Tags);
        Assert.That(manager.AppendHistoryItem(entry), Is.True);
        using var vm = ViewModel();
        await vm.LoadHistoryCommand.ExecuteAsync(null);

        vm.ShowEditItemDialogAsync = _ => Task.FromResult(true);
        await vm.EditItemCommand.ExecuteAsync(vm.HistoryItems[0]);

        HistoryItem saved = manager.GetHistoryItem(entry.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(saved.Tags, Is.EqualTo(expected));
            Assert.That(saved.AnnotationSidecarPath, Is.Null, "A line of the OCR text must not become a tag.");
            Assert.That(saved.Favorite, Is.False);
        });
    }

    [Test]
    public async Task DeleteFile_DeletesOnlyAnAnnotationFileAsTheSidecar()
    {
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        var entry = Item("image.png", 0);
        File.WriteAllBytes(entry.FilePath, [1]);
        string other = Path.Combine(_directory, "notes.txt");
        File.WriteAllText(other, "keep");
        entry.AnnotationSidecarPath = other;
        Assert.That(manager.AppendHistoryItem(entry), Is.True);
        using var vm = ViewModel();
        await vm.LoadHistoryCommand.ExecuteAsync(null);

        await vm.DeleteFileCommand.ExecuteAsync(vm.HistoryItems[0]);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(entry.FilePath), Is.False);
            Assert.That(File.Exists(other), Is.True, "A sidecar path that is not a .xann file is not deleted.");
            Assert.That(manager.GetTotalCount(), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Delete_RemovesEverySelectedEntryAfterOneConfirmation_AndDeleteFileRemovesTheirFiles()
    {
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        HistoryItem[] entries = Enumerable.Range(0, 5).Select(i => Item($"file-{i}.png", i)).ToArray();
        foreach (HistoryItem entry in entries) File.WriteAllBytes(entry.FilePath, [1]);
        File.WriteAllText(Path.Combine(_directory, "file-0.xann"), "{}");
        Assert.That(manager.AppendHistoryItems(entries), Is.True);
        using var vm = ViewModel();
        vm.IsGridView = false;
        await vm.LoadHistoryCommand.ExecuteAsync(null);

        // Newest first: file-4, file-3, file-2, file-1, file-0.
        vm.SetSelectedHistoryItems([vm.HistoryItems[0], vm.HistoryItems[1]]);
        _dialogs.ConfirmAnswer = false;
        await vm.DeleteItemCommand.ExecuteAsync(vm.HistoryItems[0]);
        Assert.Multiple(() =>
        {
            Assert.That(_dialogs.Confirmations, Has.Count.EqualTo(1));
            Assert.That(_dialogs.Confirmations[0].Title, Is.EqualTo("Delete history items?"));
            Assert.That(_dialogs.Confirmations[0].Message, Is.EqualTo("Do you really want to delete these 2 items? This action cannot be undone."));
            Assert.That(manager.GetTotalCount(), Is.EqualTo(5), "Nothing is deleted without confirmation.");
        });

        _dialogs.ConfirmAnswer = true;
        await vm.DeleteItemCommand.ExecuteAsync(vm.HistoryItems[0]);
        Assert.Multiple(() =>
        {
            Assert.That(_dialogs.Confirmations, Has.Count.EqualTo(2), "One confirmation for all selected entries.");
            Assert.That(manager.GetTotalCount(), Is.EqualTo(3));
            Assert.That(vm.HistoryItems.Select(item => item.FileName), Is.EqualTo(new[] { "file-2.png", "file-1.png", "file-0.png" }));
            Assert.That(vm.TotalItems, Is.EqualTo(3));
            Assert.That(vm.SelectedHistoryItems, Is.Empty);
            Assert.That(File.Exists(entries[4].FilePath), Is.True, "\"Delete from history\" keeps the files.");
        });

        vm.SetSelectedHistoryItems([vm.HistoryItems[1], vm.HistoryItems[2]]);
        await vm.DeleteFileCommand.ExecuteAsync(vm.HistoryItems[0]);
        Assert.Multiple(() =>
        {
            Assert.That(_dialogs.Confirmations[^1].Title, Is.EqualTo("Delete files?"));
            Assert.That(_dialogs.Confirmations[^1].Message, Is.EqualTo("Do you really want to delete this file? This action cannot be undone."),
                "Right-clicking outside the selection deletes the clicked entry only.");
            Assert.That(File.Exists(entries[2].FilePath), Is.False);
            Assert.That(manager.GetTotalCount(), Is.EqualTo(2));
        });

        await vm.DeleteFileCommand.ExecuteAsync(vm.HistoryItems[0]);
        Assert.Multiple(() =>
        {
            Assert.That(_dialogs.Confirmations[^1].Message, Is.EqualTo("Do you really want to delete these 2 files? This action cannot be undone."));
            Assert.That(File.Exists(entries[1].FilePath), Is.False);
            Assert.That(File.Exists(entries[0].FilePath), Is.False);
            Assert.That(File.Exists(Path.Combine(_directory, "file-0.xann")), Is.False, "The annotation sidecar goes with the file.");
            Assert.That(manager.GetTotalCount(), Is.EqualTo(0));
            Assert.That(vm.HistoryItems, Is.Empty);
        });
    }

    [Test]
    public async Task ImportFolder_AddsFilesDatedByLastWriteTime_WithImageOnlyAndDuplicateOptions()
    {
        string folder = Path.Combine(_directory, "import");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        string newest = Path.Combine(folder, "newest.png");
        string older = Path.Combine(folder, "sub", "older.txt");
        string oldest = Path.Combine(folder, "oldest.bin");
        File.WriteAllBytes(newest, [1]);
        File.WriteAllText(older, "text");
        File.WriteAllBytes(oldest, [1]);
        File.SetLastWriteTime(newest, new DateTime(2026, 3, 3, 12, 0, 0));
        File.SetLastWriteTime(older, new DateTime(2026, 2, 2, 12, 0, 0));
        File.SetLastWriteTime(oldest, new DateTime(2026, 1, 1, 12, 0, 0));
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        using var vm = ViewModel();
        vm.IsGridView = false;
        await vm.LoadHistoryCommand.ExecuteAsync(null);

        HistoryImportViewModel? dialog = null;
        vm.ShowImportDialogAsync = async shown =>
        {
            dialog = shown;
            Assert.Multiple(() =>
            {
                Assert.That(shown.OnlyImages, Is.False, "ShareX's default.");
                Assert.That(shown.SkipDuplicates, Is.True, "ShareX's default.");
                Assert.That(shown.ImportCommand.CanExecute(null), Is.False, "Import needs a folder.");
            });
            shown.FolderPath = Path.Combine(folder, "does-not-exist");
            await shown.ImportCommand.ExecuteAsync(null);
            Assert.That(shown.Status, Is.EqualTo("The selected folder does not exist."));
            shown.FolderPath = folder;
            shown.OnlyImages = true;
            await shown.ImportCommand.ExecuteAsync(null);
            Assert.That(shown.Status, Is.EqualTo("Successfully imported 1 files."));
            shown.OnlyImages = false;
            await shown.ImportCommand.ExecuteAsync(null);
            Assert.That(shown.Status, Is.EqualTo("Successfully imported 2 files."), "Files already in history are skipped.");
            shown.SkipDuplicates = false;
            await shown.ImportCommand.ExecuteAsync(null);
            Assert.That(shown.Status, Is.EqualTo("Successfully imported 3 files."));
        };
        await vm.ImportFolderCommand.ExecuteAsync(null);

        List<HistoryItem> all = manager.GetHistoryItems(0, int.MaxValue);
        Assert.Multiple(() =>
        {
            Assert.That(dialog, Is.Not.Null);
            Assert.That(all, Has.Count.EqualTo(6), "The last import, without skipping duplicates, added every file again.");
            Assert.That(all.Select(item => item.FileName), Is.EqualTo(new[] { "newest.png", "newest.png", "older.txt", "older.txt", "oldest.bin", "oldest.bin" }),
                "Entries are dated by the file's last write time, so the newest file comes first.");
            Assert.That(all.Select(item => item.Type), Is.EqualTo(new[] { "Image", "Image", "Text", "Text", "File", "File" }));
            Assert.That(all.Select(item => item.DateTime).Distinct(), Is.EqualTo(new[] { File.GetLastWriteTime(newest), File.GetLastWriteTime(older), File.GetLastWriteTime(oldest) }));
            Assert.That(all.Select(item => item.FilePath).Distinct(), Is.EqualTo(new[] { newest, older, oldest }));
            Assert.That(vm.HistoryItems, Has.Count.EqualTo(6), "The history is read again after an import.");
        });
    }

    [Test]
    public async Task UrlFilter_MatchesUrlSubstrings_AndResetClearsIt()
    {
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        var first = Item("a.png", 0);
        first.URL = "https://imgur.example.test/a.png";
        var second = Item("b.png", 1);
        second.URL = "https://cdn.example.test/b.png";
        Assert.That(manager.AppendHistoryItems([first, second]), Is.True);
        using var vm = ViewModel();
        vm.IsGridView = false;
        await vm.LoadHistoryCommand.ExecuteAsync(null);

        vm.UrlFilter = "IMGUR";
        await vm.SearchHistoryCommand.ExecuteAsync(null);
        Assert.That(vm.HistoryItems.Select(item => item.FileName), Is.EqualTo(new[] { "a.png" }), "The URL filter ignores case, as in ShareX.");

        vm.ResetFiltersCommand.Execute(null);
        await vm.SearchHistoryCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.UrlFilter, Is.Empty);
            Assert.That(vm.HistoryItems, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task Stats_CountTheWholeHistory_WithShareXHeadings()
    {
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        var file = Item("doc.txt", 0, "Text");
        file.Tags["ProcessName"] = "editor";
        Assert.That(manager.AppendHistoryItems([Item("a.png", 0), Item("b.png", 1), file]), Is.True);
        using var vm = ViewModel();
        vm.IsGridView = false;
        vm.FilterType = true;
        vm.SelectedType = "Text";
        await vm.LoadHistoryCommand.ExecuteAsync(null);
        Assert.That(vm.HistoryItems, Has.Count.EqualTo(1));

        string? text = null;
        var opened = new TaskCompletionSource();
        vm.ShowStatsDialogAsync = async dialog =>
        {
            Assert.That(dialog.Text, Is.EqualTo("Calculating statistics..."));
            dialog.PropertyChanged += (_, _) => { text = dialog.Text; opened.TrySetResult(); };
            await opened.Task;
        };
        await vm.ShowStatsCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("History item counts:" + Environment.NewLine + "Total: 3"), "Every entry counts, not only the filtered ones.");
            Assert.That(text, Does.Contain("Image: 2 (67%)"));
            Assert.That(text, Does.Contain("Yearly usages:" + Environment.NewLine + "2026: 3 (100%)"));
            Assert.That(text, Does.Contain("File extensions:" + Environment.NewLine + "[2] png" + Environment.NewLine + "[1] txt"));
            Assert.That(text, Does.Contain("Hosts:" + Environment.NewLine + "[3] cdn.example.test"));
            Assert.That(text, Does.Contain("Process names:" + Environment.NewLine + "[2] (empty)" + Environment.NewLine + "[1] editor"));
        });
    }

    [Test]
    public void ItemActions_EditAndRenameNeedOneEntry_AndRenameNeedsAFilePath()
    {
        using var vm = ViewModel();
        var withFile = new HistoryItem { Id = 1, FileName = "a.png", FilePath = Path.Combine(_directory, "a.png") };
        var remote = new HistoryItem { Id = 2, FileName = "remote", URL = "https://example.test/remote" };
        vm.HistoryItems.Add(withFile);
        vm.HistoryItems.Add(remote);
        var actions = new HistoryItemActions(vm, withFile);
        var remoteActions = new HistoryItemActions(vm, remote);
        Assert.Multiple(() =>
        {
            Assert.That(actions.EditTagCommand.CanExecute(null), Is.True);
            Assert.That(actions.EditItemCommand.CanExecute(null), Is.True);
            Assert.That(actions.RenameFileCommand.CanExecute(null), Is.True);
            Assert.That(actions.DeleteFileCommand.CanExecute(null), Is.True);
            Assert.That(remoteActions.RenameFileCommand.CanExecute(null), Is.False, "Renaming needs a file path with an extension.");
            Assert.That(remoteActions.DeleteFileCommand.CanExecute(null), Is.False);
        });

        vm.SetSelectedHistoryItems([withFile, remote]);
        actions.Refresh();
        remoteActions.Refresh();
        Assert.Multiple(() =>
        {
            Assert.That(actions.EditTagCommand.CanExecute(null), Is.False, "Tag and item editing act on one entry, as in ShareX.");
            Assert.That(actions.EditItemCommand.CanExecute(null), Is.False);
            Assert.That(actions.RenameFileCommand.CanExecute(null), Is.False);
            Assert.That(remoteActions.DeleteFileCommand.CanExecute(null), Is.True, "One selected entry with a file is enough.");
        });
    }

    [AvaloniaTest]
    public async Task Dialogs_LoadWithTheirViewModels()
    {
        var entry = Item("edited.png", 0);
        entry.Tags["WindowTitle"] = "Editor";
        var edit = new HistoryItemEditViewModel(entry);
        var import = new HistoryImportViewModel();
        var stats = new HistoryStatsViewModel { Text = "History item counts:" + Environment.NewLine + "Total: 1" };
        foreach ((string name, object viewModel, Control view) in new (string, object, Control)[]
        {
            ("history-edit-item", edit, new HistoryItemEditDialog { DataContext = edit }),
            ("history-import", import, new HistoryImportDialog { DataContext = import }),
            ("history-stats", stats, new HistoryStatsDialog { DataContext = stats })
        })
        {
            var window = new Window { Width = 900, Height = 760, Content = view };
            foreach (string theme in new[] { "Typography", "ThemeResources" })
            {
                window.Styles.Add(new global::Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://XerahS.UI/"))
                {
                    Source = new Uri($"avares://XerahS.UI/Themes/{theme}.axaml")
                });
            }

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var texts = view.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text ?? string.Empty).ToArray();
                var buttons = view.GetVisualDescendants().OfType<Button>().ToArray();
                switch (viewModel)
                {
                    case HistoryItemEditViewModel:
                        Assert.That(texts, Does.Contain("edited.png").And.Contain("WindowTitle=Editor").And.Contain(entry.DateTime.ToString("G")));
                        break;
                    case HistoryImportViewModel importViewModel:
                        Button importButton = buttons.Single(button => Equals(button.Content, "Import"));
                        Assert.That(importButton.IsEffectivelyEnabled, Is.False, "Import needs a folder.");
                        importViewModel.FolderPath = _directory;
                        Dispatcher.UIThread.RunJobs();
                        Assert.That(importButton.IsEffectivelyEnabled, Is.True);
                        Assert.That(view.GetVisualDescendants().OfType<CheckBox>().Select(box => box.IsChecked), Is.EqualTo(new bool?[] { false, true }));
                        break;
                    default:
                        Assert.That(texts, Does.Contain(stats.Text));
                        break;
                }

                Capture(window, name);
            }
            finally
            {
                window.Close();
            }
        }

        await Task.CompletedTask;
    }

    private static void Capture(Window window, string name)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null);
        string? directory = Environment.GetEnvironmentVariable("XERAHS_UI_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        frame.Save(file, global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private HistoryItem Item(string name, int day, string type = "Image") => new()
    {
        FileName = name,
        FilePath = Path.Combine(_directory, name),
        DateTime = new DateTime(2026, 1, 1, 23, 59, 59).AddDays(day),
        Type = type,
        Host = "cdn.example.test",
        URL = "https://cdn.example.test/" + name
    };

    private HistoryViewModel ViewModel() => new(new FakeDesktopTaskManager(), _dialogs, autoLoadHistory: false);

    private sealed class RecordingDialogService : IDialogService
    {
        public Queue<string?> InputAnswers { get; } = new();
        public string? LastInputTitle { get; private set; }
        public string? LastInputDefault { get; private set; }
        public bool ConfirmAnswer { get; set; } = true;
        public List<(string Title, string Message)> Confirmations { get; } = new();
        public List<(string Title, string Message)> Errors { get; } = new();

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<bool> ShowConfirmationAsync(string title, string message)
        {
            Confirmations.Add((title, message));
            return Task.FromResult(ConfirmAnswer);
        }

        public Task ShowErrorAsync(string title, string error)
        {
            Errors.Add((title, error));
            return Task.CompletedTask;
        }

        public Task ShowWarningAsync(string title, string warning) => Task.CompletedTask;

        public Task<string?> ShowInputAsync(string title, string label, string? defaultValue = null)
        {
            LastInputTitle = title;
            LastInputDefault = defaultValue;
            return Task.FromResult(InputAnswers.Count > 0 ? InputAnswers.Dequeue() : null);
        }

        public Task<T?> ShowSelectionAsync<T>(string title, string label, IEnumerable<T> items) where T : class => Task.FromResult<T?>(null);
    }
}
