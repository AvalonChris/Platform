using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Platform.Web.Email;

public record EmailMessage(string To, string Subject, string Body);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message);
}

public class EmailOptions
{
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "";
    public SmtpSettings Smtp { get; set; } = new();

    public bool IsSmtpConfigured => !string.IsNullOrWhiteSpace(Smtp.Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

public class SmtpSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string UserName { get; set; } = "";

    /// <summary>Keep it in user-secrets or an environment variable, never in appsettings.json.</summary>
    public string Password { get; set; } = "";

    public bool EnableSsl { get; set; } = true;
}

/// <summary>
/// Used until an email service is set up: saves each email as a text file under App_Data/emails instead of
/// sending it, so sign-in links and notices can still be read during development.
/// </summary>
public class FileEmailSender(IHostEnvironment environment, ILogger<FileEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message)
    {
        var directory = Path.Combine(environment.ContentRootPath, "App_Data", "emails");
        Directory.CreateDirectory(directory);

        var safeSubject = string.Concat(message.Subject.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{safeSubject}.txt");
        await File.WriteAllTextAsync(path, $"To: {message.To}\nSubject: {message.Subject}\n\n{message.Body}\n");

        logger.LogInformation("Email to {To} ({Subject}) was saved to {Path} instead of being sent.",
            message.To, message.Subject, path);
    }
}

public class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message)
    {
        var settings = options.Value;
        using var client = new SmtpClient(settings.Smtp.Host, settings.Smtp.Port)
        {
            EnableSsl = settings.Smtp.EnableSsl,
            Credentials = string.IsNullOrEmpty(settings.Smtp.UserName)
                ? null
                : new NetworkCredential(settings.Smtp.UserName, settings.Smtp.Password)
        };

        using var mail = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = false
        };
        mail.To.Add(message.To);

        await client.SendMailAsync(mail);
    }
}
