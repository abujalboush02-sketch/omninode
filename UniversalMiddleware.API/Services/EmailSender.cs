using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace UniversalMiddleware.API.Services;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody);
}

/// <summary>
/// Plain SMTP sender (no extra packages). Configure:
///   Smtp:Host, Smtp:Port (587), Smtp:User, Smtp:Password, Smtp:From, Smtp:EnableSsl (true)
/// If Smtp:Host is missing, the email is logged instead of sent (dev mode).
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        var host = _config["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogWarning("SMTP not configured; email to {To} with subject '{Subject}' was not sent.", to, subject);
            return;
        }

        var port = int.TryParse(_config["Smtp:Port"], out var p) ? p : 587;
        var enableSsl = !bool.TryParse(_config["Smtp:EnableSsl"], out var ssl) || ssl;
        var from = _config["Smtp:From"] ?? _config["Smtp:User"] ?? "no-reply@omninode.local";

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            Credentials = new NetworkCredential(_config["Smtp:User"], _config["Smtp:Password"])
        };

        using var message = new MailMessage(from, to, subject, htmlBody) { IsBodyHtml = true };

        try
        {
            await client.SendMailAsync(message);
        }
        catch (Exception ex)
        {
            // Never let an email failure break the request flow
            _logger.LogError(ex, "Failed to send email to {To}.", to);
        }
    }
}
