using System.Net;
using System.Net.Mail;
using Marketplace.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marketplace.Infrastructure.Services;

/// <summary>SMTP configuration. When the host is empty, sending is skipped and logged.</summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool UseSsl { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = "no-reply@marketplace.dev";

    public string FromName { get; set; } = "Marketplace";
}

/// <summary>
/// Sends transactional e-mail. When SMTP is not configured the message is logged instead
/// of sent, so development never depends on a mail server.
/// </summary>
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            logger.LogInformation("SMTP is not configured; e-mail to {To} ({Subject}) was skipped.", message.To, message.Subject);
            return;
        }

        try
        {
            using var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.UseSsl,
                Credentials = string.IsNullOrWhiteSpace(_options.Username)
                    ? CredentialCache.DefaultNetworkCredentials
                    : new NetworkCredential(_options.Username, _options.Password)
            };

            using var mail = new MailMessage
            {
                From = new MailAddress(_options.FromAddress, _options.FromName),
                Subject = message.Subject,
                Body = message.HtmlBody,
                IsBodyHtml = true
            };

            if (!string.IsNullOrWhiteSpace(message.TextBody))
            {
                mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.TextBody));
            }

            mail.To.Add(message.To);

            if (!string.IsNullOrWhiteSpace(message.ReplyTo))
            {
                mail.ReplyToList.Add(message.ReplyTo);
            }

            await client.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);
        }
        catch (SmtpException ex)
        {
            // A failed notification must never fail the business operation that caused it.
            logger.LogError(ex, "Failed to send e-mail to {To}", message.To);
        }
    }
}
