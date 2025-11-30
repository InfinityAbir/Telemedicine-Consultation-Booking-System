using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Telemed.Models;

namespace Telemed.Controllers
{
    [Authorize]
    public class InvoicesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;

        public InvoicesController(ApplicationDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        [Authorize]
        public async Task<IActionResult> DownloadInvoice(int id)
        {
            var invoice = await _db.Invoices.FindAsync(id);
            if (invoice == null) return NotFound();

            // Basic authorization: only patient or admin
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!User.IsInRole("Admin"))
            {
                var patient = await _db.Patients.FindAsync(invoice.PatientId);
                if (patient == null) return Forbid();
                if (patient.UserId != userId) return Forbid();
            }

            if (string.IsNullOrWhiteSpace(invoice.PdfFilePath)) return NotFound();

            var filePath = Path.Combine(_env.WebRootPath, invoice.PdfFilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(filePath)) return NotFound();

            var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
            return File(bytes, "application/pdf", Path.GetFileName(filePath));
        }
    }
}
