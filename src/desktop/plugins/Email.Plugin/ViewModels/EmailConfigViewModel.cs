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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using XerahS.Uploaders.PluginSystem;

namespace ShareX.Email.Plugin.ViewModels;

public partial class EmailConfigViewModel : ObservableObject, IUploaderConfigViewModel, IProviderContextAware
{
    [ObservableProperty] private string _smtpServer = "smtp.gmail.com";
    [ObservableProperty] private decimal? _smtpPort = 587;
    [ObservableProperty] private string _fromEmail = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _rememberLastTo = true;
    [ObservableProperty] private string _defaultSubject = "Sending email from XerahS";
    [ObservableProperty] private bool _automaticSend;
    [ObservableProperty] private string _automaticSendTo = string.Empty;
    [ObservableProperty] private string _passwordSummary = "No password is stored.";
    [ObservableProperty] private string? _statusMessage;

    private string _secretKey = Guid.NewGuid().ToString("N");
    private ISecretStore? _secrets;

    partial void OnPasswordChanged(string value)
    {
        PasswordSummary = string.IsNullOrEmpty(value)
            ? "No password is stored."
            : "A password is set and will be stored in the secret store.";
    }

    [RelayCommand]
    private void ClearStoredPassword()
    {
        _secrets?.DeleteSecret(EmailProvider.Id, _secretKey, EmailSharer.PasswordSecret);
        Password = string.Empty;
        StatusMessage = "The stored email password was cleared.";
    }

    public void LoadFromJson(string json)
    {
        try
        {
            EmailConfigModel config = EmailProvider.DeserializeConfig(json);
            _secretKey = string.IsNullOrWhiteSpace(config.SecretKey) ? Guid.NewGuid().ToString("N") : config.SecretKey;
            SmtpServer = config.SmtpServer ?? string.Empty;
            SmtpPort = config.SmtpPort;
            FromEmail = config.FromEmail ?? string.Empty;
            RememberLastTo = config.RememberLastTo;
            DefaultSubject = config.DefaultSubject ?? string.Empty;
            AutomaticSend = config.AutomaticSend;
            AutomaticSendTo = config.AutomaticSendTo ?? string.Empty;
            LoadPassword();
            StatusMessage = null;
        }
        catch (JsonException)
        {
            StatusMessage = "Failed to load the Email settings.";
        }
    }

    public string ToJson()
    {
        PersistPassword();
        return JsonConvert.SerializeObject(CreateConfig(), Formatting.Indented);
    }

    public bool Validate()
    {
        string? error = EmailSharer.GetConfigError(CreateConfig(), Password);
        if (error == null && AutomaticSend && string.IsNullOrWhiteSpace(AutomaticSendTo))
        {
            error = "Enter the address to send to automatically, or turn automatic sending off.";
        }

        StatusMessage = error;
        if (error != null) return false;
        PersistPassword();
        return true;
    }

    public void SetContext(IProviderContext context)
    {
        _secrets = context.Secrets;
        LoadPassword();
    }

    private EmailConfigModel CreateConfig() => new()
    {
        SecretKey = _secretKey,
        SmtpServer = SmtpServer?.Trim() ?? string.Empty,
        SmtpPort = (int)(SmtpPort ?? 0),
        FromEmail = FromEmail?.Trim() ?? string.Empty,
        RememberLastTo = RememberLastTo,
        DefaultSubject = DefaultSubject ?? string.Empty,
        AutomaticSend = AutomaticSend,
        AutomaticSendTo = AutomaticSendTo?.Trim() ?? string.Empty
    };

    private void LoadPassword()
    {
        if (_secrets == null || string.IsNullOrWhiteSpace(_secretKey)) return;
        Password = _secrets.GetSecret(EmailProvider.Id, _secretKey, EmailSharer.PasswordSecret) ?? string.Empty;
    }

    private void PersistPassword()
    {
        if (_secrets == null) return;
        if (string.IsNullOrEmpty(Password)) _secrets.DeleteSecret(EmailProvider.Id, _secretKey, EmailSharer.PasswordSecret);
        else _secrets.SetSecret(EmailProvider.Id, _secretKey, EmailSharer.PasswordSecret, Password);
    }
}
