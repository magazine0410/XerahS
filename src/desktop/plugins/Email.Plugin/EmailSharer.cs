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
using System.Net.Mail;
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;
using XerahS.Uploaders.SharingServices;

namespace ShareX.Email.Plugin;

/// <summary>
/// ShareX's EmailSharer: sends the URL by SMTP, to the automatic address or after the compose window.
/// Cancelling the window shares nothing and is not an error, as in ShareX.
/// </summary>
public sealed class EmailSharer : UrlSharer
{
    internal const string PasswordSecret = "password";
    internal const string LastToSecret = "lastTo";

    private readonly EmailConfigModel _config;
    private readonly string _password;
    private readonly ISecretStore? _secrets;

    public EmailSharer(EmailConfigModel config, string password, ISecretStore? secrets)
    {
        _config = config;
        _password = password;
        _secrets = secrets;
    }

    /// <summary>Sends the email; replaced in tests.</summary>
    internal Func<MailMessage, EmailConfigModel, string, CancellationToken, Task> Send { get; set; } = SendSmtpAsync;

    public override async Task<UploadResult> ShareURLAsync(string url, CancellationToken cancellationToken = default)
    {
        var result = new UploadResult { URL = url, IsURLExpected = false, IsSuccess = true };
        string? configError = GetConfigError(_config, _password);
        if (configError != null)
        {
            result.Errors.Add(configError);
            return result;
        }

        EmailMessageDraft? email;
        if (_config.AutomaticSend && !string.IsNullOrWhiteSpace(_config.AutomaticSendTo))
        {
            email = new EmailMessageDraft(_config.AutomaticSendTo.Trim(), _config.DefaultSubject, url);
        }
        else
        {
            var compose = UrlSharingHost.ComposeEmailAsync;
            if (compose == null)
            {
                result.Errors.Add("The email window is not available here. Turn on automatic sending in the Email settings.");
                return result;
            }

            string lastTo = _config.RememberLastTo ? _secrets?.GetSecret(EmailProvider.Id, _config.SecretKey, LastToSecret) ?? string.Empty : string.Empty;
            email = await compose(new EmailMessageDraft(lastTo, _config.DefaultSubject, url), cancellationToken).ConfigureAwait(false);
            if (email == null) return result;
            if (_config.RememberLastTo) _secrets?.SetSecret(EmailProvider.Id, _config.SecretKey, LastToSecret, email.ToEmail);
        }

        try
        {
            using var message = new MailMessage(_config.FromEmail.Trim(), email.ToEmail) { Subject = email.Subject, Body = email.Body };
            await Send(message, _config, _password, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is SmtpException or FormatException or ArgumentException or InvalidOperationException)
        {
            result.Errors.Add("The email could not be sent: " + (ex.InnerException?.Message ?? ex.Message));
        }

        return result;
    }

    /// <summary>ShareX's check: an SMTP server, port, sender, and password are required.</summary>
    internal static string? GetConfigError(EmailConfigModel config, string password)
    {
        if (string.IsNullOrWhiteSpace(config.SmtpServer) || config.SmtpPort is <= 0 or > 65535 ||
            string.IsNullOrWhiteSpace(config.FromEmail) || string.IsNullOrEmpty(password))
        {
            return "Email is not set up. Enter the SMTP server, port, sender email, and password in Destination Settings > URL Sharing Services > Email.";
        }

        return null;
    }

    // As in ShareX: SMTP with STARTTLS (EnableSsl) and the sender's credentials.
    private static async Task SendSmtpAsync(MailMessage message, EmailConfigModel config, string password, CancellationToken cancellationToken)
    {
        using var smtp = new SmtpClient(config.SmtpServer.Trim(), config.SmtpPort)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(config.FromEmail.Trim(), password)
        };
        await smtp.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
    }
}
