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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Services;

namespace XerahS.UI.ViewModels;

/// <summary>ShareX's Analyze image options window. API keys are read from and saved to the keyring.</summary>
public sealed partial class AnalyzeImageOptionsViewModel : ViewModelBase
{
    private readonly AIOptions _target;
    private readonly AnalyzeImageService _service;
    private readonly Action<AIProvider, string?> _saveApiKey;

    public IReadOnlyList<AIProvider> Providers { get; } = Enum.GetValues<AIProvider>();
    public ObservableCollection<string> OpenAIModels { get; } = ["gpt-5.2", "gpt-5.1", "gpt-5", "gpt-5-mini", "gpt-5-nano"];
    public ObservableCollection<string> GeminiModels { get; } = ["gemini-2.0-flash", "gemini-2.0-flash-lite", "gemini-1.5-flash", "gemini-1.5-pro"];
    public ObservableCollection<string> OpenRouterModels { get; } = ["openai/gpt-4o", "anthropic/claude-3.5-sonnet", "google/gemini-pro-1.5"];
    public IReadOnlyList<string> ReasoningEfforts { get; } = ["minimal", "low", "medium", "high"];
    public IReadOnlyList<string> VerbosityLevels { get; } = ["low", "medium", "high"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpenAI), nameof(IsGemini), nameof(IsOpenRouter))]
    private AIProvider _provider;

    [ObservableProperty] private string? _openAIAPIKey;
    [ObservableProperty] private string _openAIModel;
    [ObservableProperty] private string? _openAICustomURL;
    [ObservableProperty] private string _reasoningEffort;
    [ObservableProperty] private string _verbosity;
    [ObservableProperty] private string? _geminiAPIKey;
    [ObservableProperty] private string _geminiModel;
    [ObservableProperty] private string? _openRouterAPIKey;
    [ObservableProperty] private string _openRouterModel;
    [ObservableProperty] private bool _autoStartRegion;
    [ObservableProperty] private bool _autoStartAnalyze;
    [ObservableProperty] private bool _autoCopyResult;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusText = string.Empty;

    public Action<bool>? CloseRequested { get; set; }

    public bool IsOpenAI => Provider is AIProvider.OpenAI or AIProvider.OpenAILegacy;
    public bool IsGemini => Provider == AIProvider.Gemini;
    public bool IsOpenRouter => Provider == AIProvider.OpenRouter;
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    public AnalyzeImageOptionsViewModel(AIOptions target, AnalyzeImageService service,
        Func<AIProvider, string?> getApiKey, Action<AIProvider, string?> saveApiKey)
    {
        _target = target;
        _service = service;
        _saveApiKey = saveApiKey;
        _provider = target.Provider;
        _openAIAPIKey = getApiKey(AIProvider.OpenAI);
        _openAIModel = target.OpenAIModel;
        _openAICustomURL = target.OpenAICustomURL;
        _reasoningEffort = target.ReasoningEffort;
        _verbosity = target.Verbosity;
        _geminiAPIKey = getApiKey(AIProvider.Gemini);
        _geminiModel = target.GeminiModel;
        _openRouterAPIKey = getApiKey(AIProvider.OpenRouter);
        _openRouterModel = target.OpenRouterModel;
        _autoStartRegion = target.AutoStartRegion;
        _autoStartAnalyze = target.AutoStartAnalyze;
        _autoCopyResult = target.AutoCopyResult;
        AddCurrentModel(OpenAIModels, OpenAIModel);
        AddCurrentModel(GeminiModels, GeminiModel);
        AddCurrentModel(OpenRouterModels, OpenRouterModel);
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        StatusText = "Testing...";
        try
        {
            AnalyzeImageConnectionResult result = await _service.TestConnectionAsync(CreateOptions(), CurrentApiKey);
            StatusText = result.Message;
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadModelsAsync()
    {
        IsBusy = true;
        StatusText = "Loading models...";
        try
        {
            IReadOnlyList<string> models = await _service.LoadModelsAsync(CreateOptions(), CurrentApiKey);
            string current = OpenAIModel;
            OpenAIModels.Clear();
            foreach (string model in models) OpenAIModels.Add(model);
            if (models.Count > 0)
            {
                OpenAIModel = models.Contains(current) ? current : models[0];
                StatusText = $"{models.Count} models loaded.";
            }
            else
            {
                AddCurrentModel(OpenAIModels, current);
                StatusText = "No models found.";
            }
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenAPIKeyPage() => URLHelpers.OpenURL(AnalyzeImageService.GetAPIKeyPage(Provider));

    [RelayCommand]
    private void Save()
    {
        try
        {
            _saveApiKey(AIProvider.OpenAI, OpenAIAPIKey);
            _saveApiKey(AIProvider.Gemini, GeminiAPIKey);
            _saveApiKey(AIProvider.OpenRouter, OpenRouterAPIKey);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "Save Analyze image API keys");
            StatusText = "Could not save the API key: " + ex.Message;
            return;
        }

        AIOptions updated = CreateOptions();
        updated.Input = _target.Input;
        _target.CopyFrom(updated);
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    private string? CurrentApiKey => Provider switch
    {
        AIProvider.Gemini => GeminiAPIKey,
        AIProvider.OpenRouter => OpenRouterAPIKey,
        _ => OpenAIAPIKey
    };

    private AIOptions CreateOptions() => new()
    {
        Provider = Provider,
        OpenAIModel = OpenAIModel ?? string.Empty,
        OpenAICustomURL = OpenAICustomURL?.Trim() ?? string.Empty,
        ReasoningEffort = ReasoningEffort ?? "minimal",
        Verbosity = Verbosity ?? "medium",
        GeminiModel = GeminiModel ?? string.Empty,
        OpenRouterModel = OpenRouterModel ?? string.Empty,
        Input = _target.Input,
        AutoStartRegion = AutoStartRegion,
        AutoStartAnalyze = AutoStartAnalyze,
        AutoCopyResult = AutoCopyResult
    };

    private static void AddCurrentModel(ObservableCollection<string> models, string model)
    {
        if (!string.IsNullOrWhiteSpace(model) && !models.Contains(model)) models.Add(model);
    }
}
