using System.Threading.Tasks;
using Telemed.Models;

namespace Telemed.Services
{
    public interface IInvoiceService
    {
        Task<Invoice> CreateAndSaveInvoiceAsync(Invoice invoice);
        Task<byte[]> GenerateInvoicePdfBytesAsync(Invoice invoice);
    }
}
