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

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;
using XerahS.Common;
using XerahS.Core.Uploaders;
using XerahS.Uploaders.PluginSystem;

namespace XerahS.Core.Services;

public sealed record AnalyzeImageConnectionResult(bool Success, string Message);

/// <summary>
/// The Analyze image API keys, one per provider, kept in XerahS's secret store (the system keyring through
/// secret-tool on Linux). ShareX keeps them DPAPI-encrypted in its settings, which only works on Windows.
/// </summary>
public static class AIApiKeys
{
    private const string SecretProviderId = "xerahs.analyze-image";
    private const string SecretName = "apiKey";

    internal static Func<ISecretStore> Store { get; set; } = () => ProviderContextManager.EnsureProviderContext().Secrets;

    // As in ShareX, "OpenAI (legacy)" uses the OpenAI key.
    private static string SecretKey(AIProvider provider) => provider switch
    {
        AIProvider.Gemini => "provider:gemini",
        AIProvider.OpenRouter => "provider:openrouter",
        _ => "provider:openai"
    };

    public static string? Get(AIProvider provider)
    {
        try
        {
            string? key = Store().GetSecret(SecretProviderId, SecretKey(provider), SecretName);
            return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Read Analyze image API key");
            return null;
        }
    }

    public static bool Has(AIProvider provider) => Get(provider) != null;

    public static void Set(AIProvider provider, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) Store().DeleteSecret(SecretProviderId, SecretKey(provider), SecretName);
        else Store().SetSecret(SecretProviderId, SecretKey(provider), SecretName, apiKey.Trim());
    }
}

/// <summary>ShareX's AnalyzeImageService and AI providers: OpenAI, OpenAI (legacy), Gemini and OpenRouter.</summary>
public sealed class AnalyzeImageService
{
    public const string DefaultPrompt = "What is in this image?";
    public const string OpenAIDefaultURL = "https://api.openai.com";

    private readonly HttpClient _client;

    public AnalyzeImageService() : this(HttpClientFactory.Create()) { }

    public AnalyzeImageService(HttpClient client) => _client = client;

    public static string GetAPIKeyPage(AIProvider provider) => provider switch
    {
        AIProvider.Gemini => "https://aistudio.google.com/app/apikey",
        AIProvider.OpenRouter => "https://openrouter.ai/keys",
        _ => "https://platform.openai.com/api-keys"
    };

    public static string GetModel(AIOptions options) => options.Provider switch
    {
        AIProvider.Gemini => options.GeminiModel,
        AIProvider.OpenRouter => options.OpenRouterModel,
        _ => options.OpenAIModel
    };

    public async Task<string> AnalyzeAsync(SKBitmap image, AIOptions options, string? apiKey, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException(MissingKeyMessage(options.Provider));
        string prompt = string.IsNullOrWhiteSpace(options.Input) ? DefaultPrompt : options.Input;
        string model = GetModel(options)?.Trim() ?? string.Empty;
        if (model.Length == 0) throw new InvalidOperationException("Choose a model in Options.");

        return options.Provider switch
        {
            AIProvider.OpenAI => await AnalyzeWithOpenAIAsync(image, options, model, prompt, apiKey, token),
            AIProvider.OpenAILegacy => await AnalyzeWithChatCompletionsAsync(
                OpenAIURL(options, "/v1/chat/completions"), image, "image/jpeg", model, prompt, apiKey, maxTokens: null, "OpenAI", token),
            AIProvider.OpenRouter => await AnalyzeWithChatCompletionsAsync(
                "https://openrouter.ai/api/v1/chat/completions", image, "image/png", model, prompt, apiKey, maxTokens: 1024, "OpenRouter", token),
            AIProvider.Gemini => await AnalyzeWithGeminiAsync(image, model, prompt, apiKey, token),
            _ => throw new InvalidOperationException("Select a provider first.")
        };
    }

    public async Task<AnalyzeImageConnectionResult> TestConnectionAsync(AIOptions options, string? apiKey, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return new AnalyzeImageConnectionResult(false, MissingKeyMessage(options.Provider));

        using HttpRequestMessage request = CreateModelsRequest(options, apiKey);
        using HttpResponseMessage response = await _client.SendAsync(request, token);
        if (response.IsSuccessStatusCode) return new AnalyzeImageConnectionResult(true, "Connection OK.");

        string content = await response.Content.ReadAsStringAsync(token);
        return new AnalyzeImageConnectionResult(false, DescribeFailure(response, content));
    }

    /// <summary>Lists the models of an OpenAI or OpenAI-compatible server, as ShareX's "Load models" does.</summary>
    public async Task<IReadOnlyList<string>> LoadModelsAsync(AIOptions options, string? apiKey, CancellationToken token = default)
    {
        if (options.Provider is not (AIProvider.OpenAI or AIProvider.OpenAILegacy)) return [];
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException(MissingKeyMessage(options.Provider));

        using HttpRequestMessage request = CreateModelsRequest(options, apiKey);
        using HttpResponseMessage response = await _client.SendAsync(request, token);
        string content = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException(DescribeFailure(response, content));

        using JsonDocument document = JsonDocument.Parse(content);
        return document.RootElement.GetProperty("data").EnumerateArray()
            .Select(model => model.GetProperty("id").GetString())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<string> AnalyzeWithOpenAIAsync(SKBitmap image, AIOptions options, string model, string prompt, string apiKey, CancellationToken token)
    {
        // ShareX's reasoning defaults: GPT-5.x models call the lowest effort "none".
        string? effort = options.ReasoningEffort;
        if (model.StartsWith("gpt-5.", StringComparison.OrdinalIgnoreCase))
        {
            if (effort == null || effort.Equals("minimal", StringComparison.OrdinalIgnoreCase)) effort = "none";
        }
        else effort ??= "minimal";

        var body = new JsonObject
        {
            ["model"] = model,
            ["reasoning"] = new JsonObject { ["effort"] = effort },
            ["input"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(
                    new JsonObject { ["type"] = "input_text", ["text"] = prompt },
                    new JsonObject { ["type"] = "input_image", ["image_url"] = DataUri(image, "image/jpeg") })
            }),
            ["text"] = new JsonObject { ["verbosity"] = options.Verbosity ?? "medium" },
            ["store"] = false
        };

        using JsonDocument result = await PostAsync(OpenAIURL(options, "/v1/responses"), body, apiKey, "OpenAI", token);
        if (result.RootElement.TryGetProperty("output", out JsonElement output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in output.EnumerateArray())
            {
                if (item.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array &&
                    content.GetArrayLength() > 0 && content[0].TryGetProperty("text", out JsonElement text) &&
                    text.GetString() is { Length: > 0 } value)
                {
                    return value;
                }
            }
        }

        return string.Empty;
    }

    private async Task<string> AnalyzeWithChatCompletionsAsync(string url, SKBitmap image, string mimeType, string model, string prompt,
        string apiKey, int? maxTokens, string providerName, CancellationToken token)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(
                    new JsonObject { ["type"] = "text", ["text"] = prompt },
                    new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = DataUri(image, mimeType) } })
            })
        };
        if (maxTokens != null) body["max_tokens"] = maxTokens;

        using JsonDocument result = await PostAsync(url, body, apiKey, providerName, token);
        if (result.RootElement.TryGetProperty("choices", out JsonElement choices) && choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0 && choices[0].TryGetProperty("message", out JsonElement message) &&
            message.TryGetProperty("content", out JsonElement content))
        {
            return content.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private async Task<string> AnalyzeWithGeminiAsync(SKBitmap image, string model, string prompt, string apiKey, CancellationToken token)
    {
        var body = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["parts"] = new JsonArray(
                    new JsonObject { ["text"] = prompt },
                    new JsonObject { ["inline_data"] = new JsonObject { ["mime_type"] = "image/png", ["data"] = Base64(image, "image/png") } })
            })
        };

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";
        using JsonDocument result = await PostAsync(url, body, apiKey, "Gemini", token);
        return result.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString()
            ?? string.Empty;
    }

    private async Task<JsonDocument> PostAsync(string url, JsonObject body, string apiKey, string providerName, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        Authorize(request, providerName == "Gemini" ? AIProvider.Gemini : AIProvider.OpenAI, apiKey);

        using HttpResponseMessage response = await _client.SendAsync(request, token);
        string content = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Error from {providerName} API: {DescribeFailure(response, content)}");
        return JsonDocument.Parse(content);
    }

    private static HttpRequestMessage CreateModelsRequest(AIOptions options, string apiKey)
    {
        string url = options.Provider switch
        {
            AIProvider.Gemini => "https://generativelanguage.googleapis.com/v1beta/models",
            AIProvider.OpenRouter => "https://openrouter.ai/api/v1/models",
            _ => URLHelpers.CombineURL(string.IsNullOrWhiteSpace(options.OpenAICustomURL) ? OpenAIDefaultURL : options.OpenAICustomURL.Trim(), "v1/models")
        };
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        Authorize(request, options.Provider, apiKey);
        return request;
    }

    // Gemini takes its key in a header instead of ShareX's ?key= query, so the key stays out of URLs and logs.
    private static void Authorize(HttpRequestMessage request, AIProvider provider, string apiKey)
    {
        if (provider == AIProvider.Gemini) request.Headers.Add("x-goog-api-key", apiKey.Trim());
        else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
    }

    private static string OpenAIURL(AIOptions options, string path)
    {
        string url = string.IsNullOrWhiteSpace(options.OpenAICustomURL) ? OpenAIDefaultURL : options.OpenAICustomURL.Trim();
        return url.EndsWith(path, StringComparison.Ordinal) ? url : URLHelpers.CombineURL(url, path);
    }

    private static string MissingKeyMessage(AIProvider provider) => provider switch
    {
        AIProvider.Gemini => "Missing Gemini API key.",
        AIProvider.OpenRouter => "Missing OpenRouter API key.",
        _ => "Missing OpenAI API key."
    };

    private static string DescribeFailure(HttpResponseMessage response, string content)
    {
        string summary = string.IsNullOrWhiteSpace(response.ReasonPhrase)
            ? ((int)response.StatusCode).ToString()
            : $"{(int)response.StatusCode} {response.ReasonPhrase}";
        if (!string.IsNullOrWhiteSpace(content)) summary += ": " + (content.Length > 200 ? content[..200] + "..." : content);
        return summary;
    }

    private static string DataUri(SKBitmap image, string mimeType) => $"data:{mimeType};base64,{Base64(image, mimeType)}";

    /// <summary>OpenAI gets a JPEG at quality 90 and the others a PNG, as in ShareX. JPEG has no alpha, so it is flattened on white.</summary>
    internal static string Base64(SKBitmap image, string mimeType)
    {
        if (mimeType == "image/png")
        {
            using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
            return Convert.ToBase64String(png.AsSpan());
        }

        using var opaque = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(opaque))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(image, 0, 0, new SKSamplingOptions());
        }
        using SKData jpeg = opaque.Encode(SKEncodedImageFormat.Jpeg, 90);
        return Convert.ToBase64String(jpeg.AsSpan());
    }
}
