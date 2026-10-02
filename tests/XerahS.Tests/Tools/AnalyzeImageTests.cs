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
using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using NUnit.Framework;
using SkiaSharp;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Services;
using XerahS.Core.Tasks.Processors;
using XerahS.Platform.Abstractions;
using XerahS.Uploaders.PluginSystem;
using XerahS.UI.ViewModels;

namespace XerahS.Tests.Tools;

[TestFixture, NonParallelizable]
public class AnalyzeImageTests
{
    private Func<ISecretStore> _previousStore = null!;
    private MemorySecretStore _secrets = null!;

    [SetUp]
    public void SetUp()
    {
        _previousStore = AIApiKeys.Store;
        _secrets = new MemorySecretStore();
        AIApiKeys.Store = () => _secrets;
    }

    [TearDown]
    public void TearDown()
    {
        AIApiKeys.Store = _previousStore;
        CaptureJobProcessor.ShowAnalyzeImageCallback = null;
    }

    [Test]
    public void ApiKeys_AreStoredPerProvider_AndOpenAILegacyUsesTheOpenAIKey()
    {
        AIApiKeys.Set(AIProvider.OpenAI, " sk-openai ");
        AIApiKeys.Set(AIProvider.Gemini, "gemini-key");

        Assert.Multiple(() =>
        {
            Assert.That(AIApiKeys.Get(AIProvider.OpenAI), Is.EqualTo("sk-openai"));
            Assert.That(AIApiKeys.Get(AIProvider.OpenAILegacy), Is.EqualTo("sk-openai"));
            Assert.That(AIApiKeys.Get(AIProvider.Gemini), Is.EqualTo("gemini-key"));
            Assert.That(AIApiKeys.Has(AIProvider.OpenRouter), Is.False);
        });

        AIApiKeys.Set(AIProvider.OpenAI, "");
        Assert.That(AIApiKeys.Get(AIProvider.OpenAI), Is.Null);
    }

    [Test]
    public void ApiKeys_AreNotWrittenToTheSettingsJson()
    {
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(new TaskSettings().ToolsSettings.AIOptions);
        Assert.That(json, Does.Not.Contain("APIKey"));
    }

    [Test]
    public async Task OpenAI_UsesTheResponsesAPI_WithShareXsReasoningDefaults()
    {
        var handler = new RecordingHandler("""{"output":[{"type":"reasoning","content":[]},{"type":"message","content":[{"type":"output_text","text":"A cat."}]}]}""");
        var options = new AIOptions { Provider = AIProvider.OpenAI, OpenAIModel = "gpt-5.1", ReasoningEffort = "minimal", OpenAICustomURL = "http://localhost:1234" };
        using var image = new SKBitmap(4, 4);

        string result = await new AnalyzeImageService(new HttpClient(handler)).AnalyzeAsync(image, options, "sk-test");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("A cat."));
            Assert.That(handler.Request!.RequestUri!.ToString(), Is.EqualTo("http://localhost:1234/v1/responses"));
            Assert.That(handler.Request.Headers.Authorization?.ToString(), Is.EqualTo("Bearer sk-test"));
            Assert.That(handler.Body!["reasoning"]!["effort"]!.GetValue<string>(), Is.EqualTo("none"), "GPT-5.x calls the lowest effort \"none\".");
            Assert.That(handler.Body["text"]!["verbosity"]!.GetValue<string>(), Is.EqualTo("medium"));
            Assert.That(handler.Body["store"]!.GetValue<bool>(), Is.False);
            var content = handler.Body["input"]![0]!["content"]!;
            Assert.That(content[0]!["text"]!.GetValue<string>(), Is.EqualTo("What is in this image?"));
            Assert.That(content[1]!["image_url"]!.GetValue<string>(), Does.StartWith("data:image/jpeg;base64,"));
        });
    }

    [Test]
    public async Task OpenAILegacy_AndOpenRouter_UseChatCompletions()
    {
        const string response = """{"choices":[{"message":{"role":"assistant","content":"Text."}}]}""";
        using var image = new SKBitmap(4, 4);

        var legacy = new RecordingHandler(response);
        await new AnalyzeImageService(new HttpClient(legacy)).AnalyzeAsync(image,
            new AIOptions { Provider = AIProvider.OpenAILegacy, OpenAIModel = "llava", OpenAICustomURL = "http://localhost:1234/v1/chat/completions" }, "key");
        var openRouter = new RecordingHandler(response);
        string result = await new AnalyzeImageService(new HttpClient(openRouter)).AnalyzeAsync(image,
            new AIOptions { Provider = AIProvider.OpenRouter, OpenRouterModel = "openai/gpt-4o", Input = "Describe." }, "key");

        Assert.Multiple(() =>
        {
            Assert.That(legacy.Request!.RequestUri!.ToString(), Is.EqualTo("http://localhost:1234/v1/chat/completions"));
            Assert.That(legacy.Body!["messages"]![0]!["content"]![1]!["image_url"]!["url"]!.GetValue<string>(), Does.StartWith("data:image/jpeg;base64,"));
            Assert.That(legacy.Body["max_tokens"], Is.Null);
            Assert.That(openRouter.Request!.RequestUri!.ToString(), Is.EqualTo("https://openrouter.ai/api/v1/chat/completions"));
            Assert.That(openRouter.Body!["max_tokens"]!.GetValue<int>(), Is.EqualTo(1024));
            Assert.That(openRouter.Body["messages"]![0]!["content"]![0]!["text"]!.GetValue<string>(), Is.EqualTo("Describe."));
            Assert.That(openRouter.Body["messages"]![0]!["content"]![1]!["image_url"]!["url"]!.GetValue<string>(), Does.StartWith("data:image/png;base64,"));
            Assert.That(result, Is.EqualTo("Text."));
        });
    }

    [Test]
    public async Task Gemini_SendsThePngInline_WithTheKeyInAHeader()
    {
        var handler = new RecordingHandler("""{"candidates":[{"content":{"parts":[{"text":"A dog."}]}}]}""");
        using var image = new SKBitmap(4, 4);

        string result = await new AnalyzeImageService(new HttpClient(handler)).AnalyzeAsync(image,
            new AIOptions { Provider = AIProvider.Gemini, GeminiModel = "gemini-2.0-flash" }, "gemini-key");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("A dog."));
            Assert.That(handler.Request!.RequestUri!.ToString(),
                Is.EqualTo("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent"));
            Assert.That(handler.Request.Headers.GetValues("x-goog-api-key"), Is.EqualTo(new[] { "gemini-key" }));
            Assert.That(handler.Body!["contents"]![0]!["parts"]![1]!["inline_data"]!["mime_type"]!.GetValue<string>(), Is.EqualTo("image/png"));
        });
    }

    [Test]
    public void ApiErrors_IncludeTheProviderAndResponse()
    {
        var handler = new RecordingHandler("""{"error":"bad key"}""", HttpStatusCode.Unauthorized);
        using var image = new SKBitmap(4, 4);
        var error = Assert.ThrowsAsync<HttpRequestException>(() =>
            new AnalyzeImageService(new HttpClient(handler)).AnalyzeAsync(image, new AIOptions { Provider = AIProvider.OpenRouter }, "key"));
        Assert.That(error!.Message, Does.StartWith("Error from OpenRouter API: 401").And.Contain("bad key"));
    }

    [Test]
    public async Task LoadModels_ListsOpenAIModelsByName()
    {
        var handler = new RecordingHandler("""{"data":[{"id":"gpt-5-mini"},{"id":"Gpt-4o"},{"id":"o3"}]}""");
        var models = await new AnalyzeImageService(new HttpClient(handler)).LoadModelsAsync(new AIOptions { Provider = AIProvider.OpenAI }, "key");
        Assert.That(models, Is.EqualTo(new[] { "Gpt-4o", "gpt-5-mini", "o3" }));
        Assert.That(handler.Request!.RequestUri!.ToString(), Is.EqualTo("https://api.openai.com/v1/models"));
    }

    [AvaloniaTest]
    public async Task Window_AnalyzesAutomatically_CopiesTheResult_AndKeepsThePrompt()
    {
        var handler = new RecordingHandler("""{"choices":[{"message":{"content":"Line 1\nLine 2"}}]}""");
        var options = new AIOptions { Provider = AIProvider.OpenRouter, AutoCopyResult = true };
        string? copied = null;
        using var image = new SKBitmap(8, 8);
        using var viewModel = new AnalyzeImageViewModel(options, new AnalyzeImageService(new HttpClient(handler)), _ => "key", image)
        {
            CopyTextRequested = text => { copied = text; return Task.CompletedTask; }
        };

        await viewModel.InitializeAsync();
        viewModel.Prompt = "Transcribe.";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ResultText, Is.EqualTo("Line 1" + Environment.NewLine + "Line 2"));
            Assert.That(copied, Is.EqualTo(viewModel.ResultText));
            Assert.That(viewModel.ElapsedText, Does.StartWith("Time:"));
            Assert.That(options.Input, Is.EqualTo("Transcribe."), "ShareX keeps the last prompt in the options.");
        });
    }

    [AvaloniaTest]
    public async Task Window_WithoutAnApiKey_AsksForOne_AndDoesNotSend()
    {
        var handler = new RecordingHandler("{}");
        using var image = new SKBitmap(8, 8);
        using var viewModel = new AnalyzeImageViewModel(new AIOptions(), new AnalyzeImageService(new HttpClient(handler)), _ => null, image);

        await viewModel.InitializeAsync();
        await viewModel.AnalyzeCommand.ExecuteAsync(null);

        Assert.That(viewModel.CanAnalyze, Is.False);
        Assert.That(viewModel.ErrorMessage, Is.EqualTo("Configure an API key in Options before analyzing."));
        Assert.That(handler.Request, Is.Null);
    }

    [AvaloniaTest]
    public void Options_SaveWritesTheKeys_AndKeepsThePrompt_CancelChangesNothing()
    {
        var target = new AIOptions { Input = "Keep me." };
        var saved = new Dictionary<AIProvider, string?>();
        var viewModel = new AnalyzeImageOptionsViewModel(target, new AnalyzeImageService(), _ => null, (provider, key) => saved[provider] = key)
        {
            Provider = AIProvider.Gemini,
            GeminiAPIKey = "gemini-key",
            GeminiModel = "gemini-2.0-flash"
        };
        bool? closed = null;
        viewModel.CloseRequested = result => closed = result;

        viewModel.CancelCommand.Execute(null);
        Assert.That(target.Provider, Is.EqualTo(AIProvider.OpenAI));
        Assert.That(saved, Is.Empty);

        viewModel.SaveCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.True);
            Assert.That(target.Provider, Is.EqualTo(AIProvider.Gemini));
            Assert.That(target.GeminiModel, Is.EqualTo("gemini-2.0-flash"));
            Assert.That(target.Input, Is.EqualTo("Keep me."));
            Assert.That(saved[AIProvider.Gemini], Is.EqualTo("gemini-key"));
        });
    }

    [Test]
    public async Task AfterCapture_SavesTheImage_AndOpensTheWindowWithTheFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "xerahs-analyze-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settings = new TaskSettings
            {
                AfterCaptureJob = AfterCaptureTasks.AnalyzeImage,
                OverrideScreenshotsFolder = true,
                ScreenshotsFolder = directory
            };
            string? opened = null;
            CaptureJobProcessor.ShowAnalyzeImageCallback = (path, _) => { opened = path; return Task.CompletedTask; };
            using var image = new SKBitmap(10, 10);
            var info = new TaskInfo(settings) { Metadata = new(image) };

            Assert.That(await new CaptureJobProcessor().ProcessAsync(info, default), Is.True);

            Assert.That(opened, Is.Not.Null.And.EqualTo(info.FilePath));
            Assert.That(File.Exists(opened), Is.True);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingHandler(string response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public JsonNode? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Content != null) Body = JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(status) { Content = new StringContent(response) };
        }
    }

    private sealed class MemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = [];
        private static string Id(string provider, string key, string name) => $"{provider}|{key}|{name}";
        public string? GetSecret(string providerId, string secretKey, string name) => _values.GetValueOrDefault(Id(providerId, secretKey, name));
        public void SetSecret(string providerId, string secretKey, string name, string value) => _values[Id(providerId, secretKey, name)] = value;
        public void DeleteSecret(string providerId, string secretKey, string name) => _values.Remove(Id(providerId, secretKey, name));
        public bool HasSecret(string providerId, string secretKey, string name) => _values.ContainsKey(Id(providerId, secretKey, name));
    }
}
