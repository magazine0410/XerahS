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
using SkiaSharp;
using XerahS.UI.Views;
using Newtonsoft.Json;
using NUnit.Framework;
using XerahS.Core;
using XerahS.History;
using XerahS.Tests.Xip0052;
using XerahS.UI.ViewModels;

namespace XerahS.Tests.History;

[TestFixture]
[NonParallelizable]
public sealed class HistoryBrowsingTests
{
    private string _directory = null!;
    private string _originalFolder = null!;
    private HistorySettings _originalHistorySettings = null!;
    private ImageHistorySettings _originalImageSettings = null!;

    [SetUp]
    public void SetUp()
    {
        _originalFolder = SettingsManager.PersonalFolder;
        _originalHistorySettings = SettingsManager.Settings.HistorySettings;
        _originalImageSettings = SettingsManager.Settings.ImageHistorySettings;
        _directory = Path.Combine(Path.GetTempPath(), "xerahs-history-browsing-" + Guid.NewGuid().ToString("N"));
        SettingsManager.PersonalFolder = _directory;
        SettingsManager.Settings.HistorySettings = new HistorySettings();
        SettingsManager.Settings.ImageHistorySettings = new ImageHistorySettings();
        Directory.CreateDirectory(SettingsManager.HistoryFolder);
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
    public async Task Filters_SearchAllHistoryBeforePaging_AndIncludeBothBoundaryDates()
    {
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        for (int i = 0; i < 65; i++)
            Assert.That(manager.AppendHistoryItem(Item($"capture-{i}.png", i)), Is.True);
        using var vm = ViewModel();
        vm.IsGridView = false;
        vm.FilterDate = true;
        vm.FromDate = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        vm.ToDate = new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero);
        vm.FilterType = true;
        vm.SelectedType = "image";
        vm.FilterHost = true;
        vm.HostFilter = "EXAMPLE";
        await vm.SearchHistoryCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.TotalItems, Is.EqualTo(2));
            Assert.That(vm.HistoryItems.Select(item => item.FileName), Is.EqualTo(new[] { "capture-1.png", "capture-0.png" }));
            Assert.That(vm.AvailableTypes, Does.Contain("Image"));
        });

        vm.FromDate = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);
        await vm.SearchHistoryCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.IsDateRangeInvalid, Is.True);
            Assert.That(vm.HistoryItems, Is.Empty);
        });
    }

    [Test]
    public async Task GridFiltersAndLoadMore_CountOnlyMatchingFiles_WithoutLosingEarlierBatches()
    {
        SettingsManager.Settings.ImageHistorySettings.MaxItemCount = 2;
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        var images = Enumerable.Range(0, 5).Select(i => Item($"image-{i}.png", i)).ToArray();
        Assert.That(manager.AppendHistoryItems(images), Is.True);
        Assert.That(manager.AppendHistoryItem(Item("clip.mp4", 8, "Video")), Is.True);
        var remote = Item("remote.png", 9);
        remote.FilePath = string.Empty;
        Assert.That(manager.AppendHistoryItem(remote), Is.True);
        foreach (var item in images.Take(3)) File.WriteAllText(item.FilePath, "test");
        using var vm = ViewModel();
        await vm.LoadHistoryCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.TotalItems, Is.EqualTo(5));
            Assert.That(vm.HistoryItems, Has.Count.EqualTo(2));
            Assert.That(vm.CanLoadMoreItems, Is.True);
            Assert.That(vm.AutoLoadMoreItems, Is.True);
            Assert.That(vm.ThumbnailWidth, Is.EqualTo(250));
            Assert.That(vm.ThumbnailHeight, Is.EqualTo(150));
            Assert.That(vm.ShowTopThumbnailHeader, Is.True);
        });
        long firstId = vm.HistoryItems[0].Id;
        vm.SetSelectedHistoryItems([vm.HistoryItems[0]]);
        vm.LoadMoreItemsCommand.Execute(null);
        vm.LoadMoreItemsCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.HistoryItems, Has.Count.EqualTo(5));
            Assert.That(vm.HistoryItems.Select(item => item.Id).Distinct().Count(), Is.EqualTo(5));
            Assert.That(vm.HistoryItems[0].Id, Is.EqualTo(firstId));
            Assert.That(vm.SelectedHistoryItems[0].Id, Is.EqualTo(firstId));
            Assert.That(vm.CanLoadMoreItems, Is.False);
        });
        vm.HideMissingFiles = true;
        await vm.SearchHistoryCommand.ExecuteAsync(null);
        Assert.That(vm.TotalItems, Is.EqualTo(3));
        vm.ImagesOnly = false;
        vm.HideMissingFiles = false;
        vm.ImageBatchSize = 0;
        await vm.SearchHistoryCommand.ExecuteAsync(null);
        Assert.That(vm.HistoryItems, Has.Count.EqualTo(6));
    }

    [Test]
    public async Task Favorites_PersistForTheSelectedRows_AndUnfavoriteRemovesFromFavoritesView()
    {
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        var first = Item("one.png", 0);
        var second = Item("two.png", 1);
        Assert.That(manager.AppendHistoryItems([first, second]), Is.True);
        using var vm = ViewModel();
        await vm.LoadHistoryCommand.ExecuteAsync(null);
        vm.SetSelectedHistoryItems(vm.HistoryItems.ToArray());
        await vm.ToggleFavoriteCommand.ExecuteAsync(vm.HistoryItems[0]);
        Assert.Multiple(() =>
        {
            Assert.That(manager.GetHistoryItem(first.Id)!.Favorite, Is.True);
            Assert.That(manager.GetHistoryItem(second.Id)!.Favorite, Is.True);
        });
        vm.IsGridView = false;
        vm.FavoritesOnly = true;
        vm.SearchText = "does not match";
        vm.FilterType = true;
        vm.SelectedType = "Video";
        await vm.SearchHistoryCommand.ExecuteAsync(null);
        Assert.That(vm.HistoryItems, Has.Count.EqualTo(2), "ShareX's favorites list bypasses ordinary filters.");
        await vm.ToggleFavoriteCommand.ExecuteAsync(vm.HistoryItems[0]);
        Assert.That(vm.HistoryItems, Has.Count.EqualTo(1));
        using var reopened = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        Assert.That(reopened.GetHistoryItems().Count(item => item.Favorite), Is.EqualTo(1));
    }

    [Test]
    public async Task SearchAndImageOptions_SaveAndRestoreIndependentlyForEachView()
    {
        using (var vm = ViewModel())
        {
            vm.RememberSearchText = true;
            vm.SearchText = "grid search";
            vm.AutoLoadMoreItems = false;
            vm.HideMissingFiles = true;
            vm.ImagesOnly = false;
            vm.ThumbnailWidth = 320;
            vm.ThumbnailHeight = 200;
            vm.ThumbnailHeaderPosition = ImageHistoryThumbnailHeaderPosition.Bottom;
            vm.ImageBatchSize = 100;
            await vm.SearchHistoryCommand.ExecuteAsync(null);
            vm.IsGridView = false;
            Assert.That(vm.SearchText, Is.Empty);
            vm.RememberSearchText = true;
            vm.SearchText = "list search";
            await vm.SearchHistoryCommand.ExecuteAsync(null);
        }
        var saved = JsonConvert.DeserializeObject<ApplicationConfig>(File.ReadAllText(SettingsManager.ApplicationConfigFilePath))!;
        SettingsManager.Settings.HistorySettings = saved.HistorySettings;
        SettingsManager.Settings.ImageHistorySettings = saved.ImageHistorySettings;
        using var restored = ViewModel();
        Assert.Multiple(() =>
        {
            Assert.That(restored.SearchText, Is.EqualTo("grid search"));
            Assert.That(restored.AutoLoadMoreItems, Is.False);
            Assert.That(restored.HideMissingFiles, Is.True);
            Assert.That(restored.ImagesOnly, Is.False);
            Assert.That(restored.ThumbnailWidth, Is.EqualTo(320));
            Assert.That(restored.ThumbnailHeight, Is.EqualTo(200));
            Assert.That(restored.ShowBottomThumbnailHeader, Is.True);
            Assert.That(restored.ImageBatchSize, Is.EqualTo(100));
        });
        restored.IsGridView = false;
        Assert.That(restored.SearchText, Is.EqualTo("list search"));
        restored.RememberSearchText = false;
        await restored.SearchHistoryCommand.ExecuteAsync(null);
        var disabled = JsonConvert.DeserializeObject<ApplicationConfig>(File.ReadAllText(SettingsManager.ApplicationConfigFilePath))!;
        Assert.That(disabled.HistorySettings.SearchText, Is.Empty);
    }

    [Test]
    public async Task Search_KeepsOcrMatchesAndSupportsShareXFilenameWildcards()
    {
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        var first = Item("capture-01.png", 0);
        var second = Item("unrelated.png", 1);
        Assert.That(manager.AppendHistoryItems([first, second]), Is.True);
        new HistoryOcrIndexStore(SettingsManager.GetHistoryFilePath()).UpsertText(second.Id, second.FilePath, null, "roadmap review", "test", "en");
        using var vm = ViewModel();
        vm.SearchText = "roadmap";
        await vm.SearchHistoryCommand.ExecuteAsync(null);
        Assert.That(vm.HistoryItems.Single().Id, Is.EqualTo(second.Id));
        vm.SearchText = "CAPTURE-??.*";
        await vm.SearchHistoryCommand.ExecuteAsync(null);
        Assert.That(vm.HistoryItems.Single().Id, Is.EqualTo(first.Id));
    }

    [Test]
    public async Task ListPagingAndRefresh_ClampThePageAfterRowsAreRemoved()
    {
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        Assert.That(manager.AppendHistoryItems(Enumerable.Range(0, 3).Select(i => Item($"item-{i}.png", i))), Is.True);
        using var vm = ViewModel();
        vm.IsGridView = false;
        vm.PageSize = 2;
        await vm.SearchHistoryCommand.ExecuteAsync(null);
        vm.NextPageCommand.Execute(null);
        Assert.That(vm.HistoryItems, Has.Count.EqualTo(1));
        manager.Delete(vm.HistoryItems[0]);
        await vm.RefreshHistoryCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.CurrentPage, Is.EqualTo(1));
            Assert.That(vm.HistoryItems, Has.Count.EqualTo(2));
            Assert.That(vm.TotalItems, Is.EqualTo(2));
        });
    }

    [Test]
    public void ItemActions_DisableMissingContent_AndUseSelectionOnlyWhenItContainsTheClickedRow()
    {
        using var vm = ViewModel();
        var missing = new HistoryItem { FileName = "missing.png", FilePath = Path.Combine(_directory, "missing.png") };
        var remote = new HistoryItem { FileName = "remote.png", URL = "https://example.test/remote.png" };
        vm.HistoryItems.Add(missing);
        vm.HistoryItems.Add(remote);
        var actions = new HistoryItemActions(vm, missing);
        Assert.Multiple(() =>
        {
            Assert.That(actions.CopyCommand.CanExecute(HistoryCopyFormat.File), Is.False);
            Assert.That(actions.CopyCommand.CanExecute(HistoryCopyFormat.ImageDimensions), Is.False);
            Assert.That(actions.CopyCommand.CanExecute(HistoryCopyFormat.URL), Is.False);
            Assert.That(actions.OpenShortenedUrlCommand.CanExecute(null), Is.False);
            Assert.That(actions.ShowErrorsCommand.CanExecute(null), Is.False);
        });
        vm.SetSelectedHistoryItems([remote]);
        Assert.That(actions.CopyCommand.CanExecute(HistoryCopyFormat.URL), Is.False, "Right-clicking outside the selection targets the clicked item.");
        vm.SetSelectedHistoryItems([missing, remote]);
        actions.Refresh();
        Assert.That(actions.CopyCommand.CanExecute(HistoryCopyFormat.URL), Is.True);
        Assert.That(actions.CopyCommand.CanExecute(HistoryCopyFormat.MarkdownLinkedImage), Is.False);
    }

    [AvaloniaTest]
    public async Task View_BindsOptionsAndThumbnailSize_AndAutomaticallyFillsTheViewport()
    {
        SettingsManager.Settings.ImageHistorySettings.MaxItemCount = 2;
        SettingsManager.Settings.ImageHistorySettings.AutoLoadMoreItems = false;
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        using var image = new SKBitmap(64, 48);
        image.Erase(SKColors.SteelBlue);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        for (int i = 0; i < 10; i++)
        {
            var item = Item($"Screenshot-{i + 1:00}.png", i);
            using (var file = File.Create(item.FilePath)) encoded.SaveTo(file);
            Assert.That(manager.AppendHistoryItem(item), Is.True);
        }
        using var vm = ViewModel();
        var application = Application.Current!;
        // This test app has no shell resources; the shared menu has its own command tests.
        application.Resources["HistoryItemMenuFlyout"] = new MenuFlyout();
        var view = new HistoryView(vm);
        var window = new Window { Width = 1100, Height = 800, Content = view };
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
            await vm.SearchHistoryCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(vm.HistoryItems, Has.Count.EqualTo(2));
            vm.ThumbnailWidth = 320;
            vm.ThumbnailHeight = 180;
            vm.ThumbnailHeaderPosition = ImageHistoryThumbnailHeaderPosition.Bottom;
            await vm.SearchHistoryCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            var card = view.GetVisualDescendants().OfType<Border>().First(control => control.Classes.Contains("history-grid-item"));
            Assert.That(card.Width, Is.EqualTo(346));
            Assert.That(card.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text ?? string.Empty),
                Has.None.Contains("2026-"), "As in ShareX, the header is only the file name.");
            vm.AutoLoadMoreItems = true;
            await vm.SearchHistoryCommand.ExecuteAsync(null);
            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Multiple(() =>
            {
                Assert.That(vm.HistoryItems.Count, Is.GreaterThan(2));
                Assert.That(vm.HistoryItems.Select(item => item.Id).Distinct().Count(), Is.EqualTo(vm.HistoryItems.Count));
            });
            var filterPanel = view.GetVisualDescendants().OfType<Expander>().Single();
            filterPanel.IsExpanded = true;
            Capture(window, "history-grid");
            var options = view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Options"));
            options.Flyout!.ShowAt(options);
            Dispatcher.UIThread.RunJobs();
            Capture(window, "history-options");
            options.Flyout.Hide();
            vm.IsGridView = false;
            await vm.SearchHistoryCommand.ExecuteAsync(null);
            Capture(window, "history-list");
        }
        finally
        {
            window.Close();
            application.Resources.Remove("HistoryItemMenuFlyout");
        }
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

    [Test]
    public async Task Favorite_UpdatesTheStarInPlace_KeepingTheLoadedBatchesAndSelection()
    {
        SettingsManager.Settings.ImageHistorySettings.MaxItemCount = 2;
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        Assert.That(manager.AppendHistoryItems(Enumerable.Range(0, 5).Select(i => Item($"image-{i}.png", i))), Is.True);
        using var vm = ViewModel();
        await vm.LoadHistoryCommand.ExecuteAsync(null);
        vm.LoadMoreItemsCommand.Execute(null);
        HistoryItem target = vm.HistoryItems[^1];
        vm.SetSelectedHistoryItems([target]);
        var changed = new List<string?>();
        target.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.ToggleFavoriteCommand.ExecuteAsync(target);

        Assert.Multiple(() =>
        {
            Assert.That(vm.HistoryItems, Has.Count.EqualTo(4), "Favoriting used to reload the grid back to its first batch.");
            Assert.That(vm.HistoryItems[^1], Is.SameAs(target));
            Assert.That(vm.SelectedHistoryItems, Is.EqualTo(new[] { target }));
            Assert.That(target.Favorite, Is.True);
            Assert.That(changed, Is.EqualTo(new[] { nameof(HistoryItem.Favorite) }));
            Assert.That(manager.GetHistoryItem(target.Id)!.Favorite, Is.True);
        });
    }

    [Test]
    public async Task Delete_KeepsTheLoadedGridBatches_AndTopsUpTheListPage()
    {
        SettingsManager.Settings.ImageHistorySettings.MaxItemCount = 2;
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        Assert.That(manager.AppendHistoryItems(Enumerable.Range(0, 6).Select(i => Item($"image-{i}.png", i))), Is.True);
        using var vm = ViewModel();
        await vm.LoadHistoryCommand.ExecuteAsync(null);
        vm.LoadMoreItemsCommand.Execute(null);
        HistoryItem next = vm.HistoryItems[1];
        await vm.DeleteItemCommand.ExecuteAsync(vm.HistoryItems[0]);
        Assert.Multiple(() =>
        {
            Assert.That(vm.HistoryItems, Has.Count.EqualTo(3));
            Assert.That(vm.HistoryItems[0], Is.SameAs(next));
            Assert.That(vm.TotalItems, Is.EqualTo(5));
            Assert.That(vm.CanLoadMoreItems, Is.True);
        });

        vm.IsGridView = false;
        vm.PageSize = 2;
        await vm.SearchHistoryCommand.ExecuteAsync(null);
        string[] expected = vm.HistoryItems.Skip(1).Select(item => item.FileName).ToArray();
        await vm.DeleteItemCommand.ExecuteAsync(vm.HistoryItems[0]);
        Assert.Multiple(() =>
        {
            Assert.That(vm.HistoryItems, Has.Count.EqualTo(2), "The page is topped up from the next one.");
            Assert.That(vm.HistoryItems[0].FileName, Is.EqualTo(expected[0]));
            Assert.That(vm.TotalItems, Is.EqualTo(4));
            Assert.That(manager.GetTotalCount(), Is.EqualTo(4));
        });
    }

    [Test]
    public async Task FilterChanges_UseTheRowsAlreadyRead_UntilRefresh()
    {
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        Assert.That(manager.AppendHistoryItem(Item("first.png", 0)), Is.True);
        using var vm = ViewModel();
        await vm.LoadHistoryCommand.ExecuteAsync(null);
        Assert.That(manager.AppendHistoryItem(Item("added-later.png", 1)), Is.True);

        vm.SearchText = "*.png";
        await vm.SearchHistoryCommand.ExecuteAsync(null);
        Assert.That(vm.HistoryItems.Select(item => item.FileName), Is.EqualTo(new[] { "first.png" }),
            "As in ShareX, filtering does not read the history database again.");

        await vm.RefreshHistoryCommand.ExecuteAsync(null);
        Assert.That(vm.HistoryItems, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task PlainSearch_MatchesTagValuesButNotTagNames()
    {
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        var uploaded = Item("uploaded.png", 0);
        uploaded.Tags["UploaderInstanceId"] = "instance-42";
        var favorite = Item("starred.png", 1);
        favorite.Favorite = true;
        Assert.That(manager.AppendHistoryItems([uploaded, favorite]), Is.True);
        using var vm = ViewModel();

        foreach (string tagName in new[] { "uploader", "favorite" })
        {
            vm.SearchText = tagName;
            await vm.SearchHistoryCommand.ExecuteAsync(null);
            Assert.That(vm.HistoryItems, Is.Empty, $"\"{tagName}\" is only a tag name.");
        }

        vm.SearchText = "INSTANCE-42";
        await vm.SearchHistoryCommand.ExecuteAsync(null);
        Assert.That(vm.HistoryItems.Single().Id, Is.EqualTo(uploaded.Id));
    }

    [Test]
    public void PlainSearch_SkipsRowsWhoseTagsAreNotValidJson()
    {
        using var manager = new HistoryManagerSQLite(SettingsManager.GetHistoryFilePath());
        var broken = Item("broken.png", 0);
        var match = Item("match.png", 1);
        match.Tags["Note"] = "needle";
        Assert.That(manager.AppendHistoryItems([broken, match]), Is.True);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={SettingsManager.GetHistoryFilePath()}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE History SET Tags = 'needle, not JSON' WHERE Id = $id";
            command.Parameters.AddWithValue("$id", broken.Id);
            Assert.That(command.ExecuteNonQuery(), Is.EqualTo(1));
        }

        Assert.That(manager.SearchHistoryItemIds("needle"), Is.EqualTo(new[] { match.Id }));
    }

    [Test]
    public async Task ResetFilters_KeepsFavoritesAndTheSearchText_LikeShareX()
    {
        using var vm = ViewModel();
        vm.IsGridView = false;
        vm.FavoritesOnly = true;
        vm.SearchText = "keep me";
        vm.FilterDate = true;
        vm.FilterType = true;
        vm.SelectedType = "Image";
        vm.FilterHost = true;
        vm.HostFilter = "example";
        vm.ResetFiltersCommand.Execute(null);
        await vm.SearchHistoryCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.FavoritesOnly, Is.True);
            Assert.That(vm.SearchText, Is.EqualTo("keep me"));
            Assert.That(vm.FilterDate || vm.FilterType || vm.FilterHost, Is.False);
            Assert.That(vm.SelectedType, Is.Null);
            Assert.That(vm.HostFilter, Is.Empty);
        });
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

    private static HistoryViewModel ViewModel() => new(new FakeDesktopTaskManager(), new FakeDialogService(), autoLoadHistory: false);
}
