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

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;
using NUnit.Framework;
using XerahS.Uploaders;
using XerahS.Uploaders.CustomUploader;
using XerahS.Uploaders.PluginSystem;

namespace XerahS.Tests.Uploaders;

[TestFixture]
[NonParallelizable]
public class UploadHotkeyHttpTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task CustomTextAndShortener_SendLiteralInput(bool shorten)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var uploader = CreateUploader(port);
        const string input = "https://example.test/path?q=a&other=b";
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(input));
        var upload = shorten
            ? UploaderUploadAdapter.ShortenAsync(uploader, input, CancellationToken.None)
            : UploadTextAsync(uploader, content);
        using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var stream = client.GetStream();
        string body = await ReadRequestBodyAsync(stream);
        byte[] response = Encoding.ASCII.GetBytes("https://short.test/result");
        byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {response.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers);
        await stream.WriteAsync(response);
        var result = await upload.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(HttpUtility.ParseQueryString(body)["value"], Is.EqualTo(input));
        Assert.That(shorten ? result.ShortenedURL : result.URL, Is.EqualTo("https://short.test/result"));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task StopAllUploads_AbortsLegacyHttpRequest(bool shorten, bool responseStarted)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var uploader = CreateUploader(((IPEndPoint)listener.LocalEndpoint).Port);
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("cancel me"));
        var upload = shorten
            ? UploaderUploadAdapter.ShortenAsync(uploader, "https://example.test/long", CancellationToken.None)
            : UploadTextAsync(uploader, content);
        using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await ReadRequestBodyAsync(client.GetStream());
        if (responseStarted)
        {
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\na"));
            Assert.That(() => uploader.IsUploading, Is.False.After(5000, 10),
                "The uploader should have received the headers and be reading the incomplete response body.");
        }
        UploadCancellationScope.CancelAll();
        Exception? failure = null;
        try
        {
            await upload.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        Assert.That(failure, Is.InstanceOf<OperationCanceledException>());
    }

    private static CustomUploaderExecutor CreateUploader(int port)
    {
        var item = CustomUploaderItem.Init();
        item.RequestURL = $"http://127.0.0.1:{port}/upload";
        item.RequestMethod = XerahS.Uploaders.HttpMethod.POST;
        item.Body = CustomUploaderBody.FormURLEncoded;
        item.Arguments = new Dictionary<string, string> { ["value"] = "{input}" };
        item.URL = "{response}";
        return new CustomUploaderExecutor(item);
    }

    private static async Task<UploadResult> UploadTextAsync(CustomUploaderExecutor uploader, Stream content)
    {
        var outcome = await UploaderUploadAdapter.UploadAsync(uploader, new UploadRequest
        {
            Content = content,
            FileName = "text.txt",
            Category = UploaderCategory.Text
        }, CancellationToken.None);
        return outcome.ToUploadResult();
    }

    private static async Task<string> ReadRequestBodyAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        int length = 0;
        while (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) is { Length: > 0 } line)
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                length = int.Parse(line["Content-Length:".Length..].Trim());
        }
        var body = new char[length];
        int read = await reader.ReadBlockAsync(body.AsMemory()).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(read, Is.EqualTo(length));
        return new string(body);
    }
}
