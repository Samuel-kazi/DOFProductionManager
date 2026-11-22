using System.Net;
using System.Net.Mail;
using DOF.Application.Common.Abstractions;
using Microsoft.Extensions.Options;

namespace DOF.Infrastructure.Reminders;

/// <summary>
/// E-mail settings. The password comes from user-secrets or the environment (Email__Password), never from
/// a committed file. Without a host, e-mail is unavailable and cannot be chosen.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "DOF Production Manager";
}

public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.Host) && !string.IsNullOrWhiteSpace(options.Value.FromAddress);

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var o = options.Value;
        if (!IsConfigured)
        {
            throw new InvalidOperationException("E-mail is not configured (Email:Host and Email:FromAddress).");
        }

        using var client = new SmtpClient(o.Host, o.Port) { EnableSsl = o.UseSsl };
        if (!string.IsNullOrEmpty(o.Username))
        {
            client.Credentials = new NetworkCredential(o.Username, o.Password);
        }
        using var mail = new MailMessage(new MailAddress(o.FromAddress!, o.FromName), new MailAddress(message.To, message.ToName))
        {
            Subject = message.Subject,
            Body = message.Body + "\n\n— DOF Production Manager",
        };
        await client.SendMailAsync(mail, ct);
    }
}
