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
using XerahS.Uploaders.PluginSystem;

namespace XerahS.Uploaders.SharingServices;

/// <summary>No settings: a link service only needs the URL.</summary>
public sealed class LinkSharingConfigModel
{
}

/// <summary>
/// ShareX's SimpleURLSharingService: opens the service's share page in the browser with the URL
/// filled in. Built in, and added to Destination Settings automatically (see EnsureInstances).
/// </summary>
public abstract class LinkSharingProvider : UploaderProviderBase
{
    /// <summary>The share page; {0} is the encoded URL.</summary>
    public abstract string URLFormatString { get; }

    public override Version Version => new(1, 0, 0);
    public override UploaderCategory[] SupportedCategories => [UploaderCategory.UrlSharing];
    public override Type ConfigModelType => typeof(LinkSharingConfigModel);

    public override Uploader CreateInstance(string settingsJson) => new LinkSharer(URLFormatString);

    public override bool ValidateSettings(string settingsJson) => true;

    public override string GetDefaultSettings(UploaderCategory category) => "{}";

    public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes() =>
        new() { [UploaderCategory.UrlSharing] = [] };

    /// <summary>The display name the catalog gives an instance, so seeded and added ones match.</summary>
    public string InstanceDisplayName => $"{Name} ({UploaderCategory.UrlSharing})";

    /// <summary>
    /// Adds an instance for each built-in link service the first time it is seen. A service the
    /// user removed afterwards is not added back.
    /// </summary>
    public static int EnsureInstances()
    {
        ProviderCatalog.InitializeBuiltInProviders();
        List<LinkSharingProvider> providers = ProviderCatalog.GetAllProviders().OfType<LinkSharingProvider>()
            .OrderBy(provider => provider.Order).ToList();
        return InstanceManager.Instance.AddInstancesOnce(UploaderCategory.UrlSharing,
            providers.Select(provider => (provider.ProviderId, provider.InstanceDisplayName)));
    }

    /// <summary>ShareX's order in its URL sharing services list.</summary>
    protected abstract int Order { get; }
}

public sealed class LinkSharer : UrlSharer
{
    public LinkSharer(string urlFormatString)
    {
        URLFormatString = urlFormatString;
    }

    public string URLFormatString { get; }

    public string GetShareURL(string url) => string.Format(URLFormatString, URLHelpers.URLEncode(url));

    public override Task<UploadResult> ShareURLAsync(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new UploadResult { URL = url, IsURLExpected = false, IsSuccess = true };
        if (!UrlSharingHost.OpenUrl(GetShareURL(url)))
        {
            result.Errors.Add("The share page could not be opened.");
        }
        return Task.FromResult(result);
    }
}

// ShareX's link services, in its order. Reddit, Pinterest and VK use https rather than ShareX's http.
public sealed class FacebookSharingProvider : LinkSharingProvider
{
    public override string ProviderId => "share-facebook";
    public override string Name => "Facebook";
    public override string Description => "Share the URL on Facebook";
    public override string URLFormatString => "https://www.facebook.com/sharer/sharer.php?u={0}";
    protected override int Order => 1;
}

public sealed class RedditSharingProvider : LinkSharingProvider
{
    public override string ProviderId => "share-reddit";
    public override string Name => "Reddit";
    public override string Description => "Submit the URL to Reddit";
    public override string URLFormatString => "https://www.reddit.com/submit?url={0}";
    protected override int Order => 2;
}

public sealed class PinterestSharingProvider : LinkSharingProvider
{
    public override string ProviderId => "share-pinterest";
    public override string Name => "Pinterest";
    public override string Description => "Pin the URL on Pinterest";
    public override string URLFormatString => "https://pinterest.com/pin/create/button/?url={0}&media={0}";
    protected override int Order => 3;
}

public sealed class TumblrSharingProvider : LinkSharingProvider
{
    public override string ProviderId => "share-tumblr";
    public override string Name => "Tumblr";
    public override string Description => "Share the URL on Tumblr";
    public override string URLFormatString => "https://www.tumblr.com/share?v=3&u={0}";
    protected override int Order => 4;
}

public sealed class LinkedInSharingProvider : LinkSharingProvider
{
    public override string ProviderId => "share-linkedin";
    public override string Name => "LinkedIn";
    public override string Description => "Share the URL on LinkedIn";
    public override string URLFormatString => "https://www.linkedin.com/shareArticle?url={0}";
    protected override int Order => 5;
}

public sealed class VkSharingProvider : LinkSharingProvider
{
    public override string ProviderId => "share-vk";
    public override string Name => "VK";
    public override string Description => "Share the URL on VK";
    public override string URLFormatString => "https://vk.com/share.php?url={0}";
    protected override int Order => 6;
}

public sealed class GoogleLensSharingProvider : LinkSharingProvider
{
    public override string ProviderId => "share-google-lens";
    public override string Name => "Google Lens";
    public override string Description => "Search for the image at the URL with Google Lens";
    public override string URLFormatString => "https://lens.google.com/uploadbyurl?url={0}";
    protected override int Order => 7;
}

public sealed class BingVisualSearchSharingProvider : LinkSharingProvider
{
    public override string ProviderId => "share-bing-visual-search";
    public override string Name => "Bing Visual Search";
    public override string Description => "Search for the image at the URL with Bing Visual Search";
    public override string URLFormatString => "https://www.bing.com/images/search?view=detailv2&iss=sbi&q=imgurl:{0}";
    protected override int Order => 8;
}
