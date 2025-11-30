using System.Threading.Tasks;

namespace Telemed.Services
{
    public interface IEmailSenderExtended
    {
        Task SendEmailWithAttachmentAsync(
            string toEmail,
            string subject,
            string htmlBody,
            byte[]? attachmentBytes = null,
            string? attachmentFileName = null
        );
    }
}
