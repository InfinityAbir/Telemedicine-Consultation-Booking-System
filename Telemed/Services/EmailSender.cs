using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Telemed.Services
{
    public class EmailSenderService : IEmailSender
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailSenderService> _logger; // <- updated

        public EmailSenderService(IConfiguration config, ILogger<EmailSenderService> logger) // <- updated
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            // Support both "Smtp" section (preferred) and legacy "Email" section (your current file)
            var smtpSection = _config.GetSection("Smtp");
            var emailSection = _config.GetSection("Email");

            string host = smtpSection.GetValue<string>("Host")
                          ?? emailSection.GetValue<string>("SmtpHost");

            int port = smtpSection.GetValue<int?>("Port")
                       ?? (emailSection.GetValue<int?>("SmtpPort") ?? 587);

            string username = smtpSection.GetValue<string>("Username")
                              ?? emailSection.GetValue<string>("SmtpUser");

            string password = smtpSection.GetValue<string>("Password")
                              ?? emailSection.GetValue<string>("SmtpPass");

            bool enableSsl = smtpSection.GetValue<bool?>("EnableSsl")
                             ?? emailSection.GetValue<bool?>("EnableSsl")
                             ?? true;

            string fromAddress = smtpSection.GetValue<string>("From")
                                 ?? emailSection.GetValue<string>("FromEmail")
                                 ?? username;

            string fromName = smtpSection.GetValue<string>("FromName")
                              ?? emailSection.GetValue<string>("FromName")
                              ?? "TeleMed";

            if (string.IsNullOrWhiteSpace(host))
            {
                _logger.LogError("SMTP host is not configured. Please set Smtp:Host or Email:SmtpHost in appsettings.");
                throw new InvalidOperationException("SMTP host is not configured.");
            }

            using var message = new MailMessage()
            {
                From = new MailAddress(fromAddress, fromName),
                Subject = subject,
                Body = htmlMessage,
                IsBodyHtml = true
            };
            message.To.Add(email);

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };

            if (!string.IsNullOrWhiteSpace(username))
                client.Credentials = new NetworkCredential(username, password);

            try
            {
                // Send async
                await client.SendMailAsync(message);
                _logger.LogInformation("Email sent to {Email} via {Host}:{Port}", email, host, port);
            }
            catch (SmtpException ex)
            {
                _logger.LogError(ex, "SMTP error sending email to {Email}: {Message}", email, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while sending email to {Email}: {Message}", email, ex.Message);
                throw;
            }
        }
    }
}
