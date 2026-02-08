using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Telemed.Models;
using Telemed.ViewModels;

namespace Telemed.Controllers
{
    [Authorize]
    public class PrescriptionsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PrescriptionsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Prescriptions (Visible to both doctor & patient)
        public async Task<IActionResult> Index()
        {
            var userEmail = User.Identity?.Name;

            IQueryable<Prescription> prescriptions = _context.Prescriptions
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Patient)
                        .ThenInclude(pu => pu.User)
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Doctor)
                        .ThenInclude(d => d.User);

            // If logged in user is a patient, show only their prescriptions
            if (User.IsInRole("Patient"))
            {
                prescriptions = prescriptions
                    .Where(p => p.Appointment.Patient.User.Email == userEmail);
            }
            // If doctor, show prescriptions they created
            else if (User.IsInRole("Doctor"))
            {
                prescriptions = prescriptions
                    .Where(p => p.Appointment.Doctor.User.Email == userEmail);
            }

            return View(await prescriptions.ToListAsync());
        }

        // GET: Prescriptions/Create (Only for Doctor)
        [Authorize(Roles = "Doctor")]
        public IActionResult Create()
        {
            ViewData["AppointmentId"] = new SelectList(
                _context.Appointments
                    .Include(a => a.Patient)
                        .ThenInclude(p => p.User)
                    .Include(a => a.Doctor)
                        .ThenInclude(d => d.User)
                    .Where(a => a.Status == AppointmentStatus.Approved)
                    .Select(a => new
                    {
                        a.AppointmentId,
                        DisplayName = a.Patient.User.FullName + " - " + a.ScheduledAt.ToString("yyyy-MM-dd HH:mm")
                    }),
                "AppointmentId", "DisplayName"
            );

            return View();
        }

        // POST: Prescriptions/Create (Only for Doctor)
        [HttpPost]
        [Authorize(Roles = "Doctor")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PrescriptionViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                ViewData["AppointmentId"] = new SelectList(
                    _context.Appointments,
                    "AppointmentId", "AppointmentId",
                    vm.AppointmentId
                );
                return View(vm);
            }

            var prescription = new Prescription
            {
                AppointmentId = vm.AppointmentId,
                MedicineName = vm.MedicineName,
                Dosage = vm.Dosage,
                Duration = vm.Duration,
                Notes = vm.Notes,
                CreatedAt = DateTime.Now
            };

            _context.Prescriptions.Add(prescription);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // Generate PDF (for doctor and patient) with logo and thank-you message
        public IActionResult Pdf(int id)
        {
            var p = _context.Prescriptions
                .Include(x => x.Appointment)
                    .ThenInclude(a => a.Patient)
                        .ThenInclude(pu => pu.User)
                .Include(x => x.Appointment)
                    .ThenInclude(a => a.Doctor)
                        .ThenInclude(d => d.User)
                .FirstOrDefault(x => x.PrescriptionId == id);

            if (p == null) return NotFound();

            // Path for TeleMed logo (wwwroot/images/telemed_logo.png)
            var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "telemed_logo.png");

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(12).FontFamily("Helvetica"));

                    // Header with logo and title
                    page.Header().Row(row =>
                    {
                        if (System.IO.File.Exists(logoPath))
                        {
                            row.ConstantItem(60).Image(logoPath);
                        }

                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("TeleMed - Digital Prescription")
                                .FontSize(20).SemiBold().FontColor(Colors.Blue.Medium);
                            col.Item().Text("Online Healthcare & Consultation Platform")
                                .FontSize(10).FontColor(Colors.Grey.Darken1);
                        });
                    });

                    // Prescription content
                    page.Content().PaddingVertical(20).Column(col =>
                    {
                        col.Spacing(8);

                        col.Item().Text($"Prescription ID: {p.PrescriptionId}").Bold();
                        col.Item().Text($"Date Issued: {p.CreatedAt:yyyy-MM-dd}");
                        col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        col.Item().Text($"Patient: {p.Appointment.Patient.User.FullName}")
                            .FontSize(14).SemiBold();
                        col.Item().Text($"Doctor: {p.Appointment.Doctor.User.FullName} ({p.Appointment.Doctor.Specialization})");
                        col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        col.Item().Text("Prescription Details")
                            .FontSize(14).SemiBold().Underline();

                        col.Item().Text($"Medicine: {p.MedicineName}");
                        col.Item().Text($"Dosage: {p.Dosage}");
                        col.Item().Text($"Duration: {p.Duration}");

                        if (!string.IsNullOrWhiteSpace(p.Notes))
                        {
                            col.Item().Text("Notes:").FontSize(13).SemiBold();
                            col.Item().Text(p.Notes);
                        }

                        col.Item().PaddingTop(30).AlignCenter().Text("Thank you for using TeleMed!")
                            .FontSize(13).Italic().FontColor(Colors.Blue.Medium);
                    });

                    // Footer
                    page.Footer()
                        .AlignCenter()
                        .Text($"© {DateTime.Now.Year} TeleMed | Doctor: {p.Appointment.Doctor.User.FullName}")
                        .FontSize(10)
                        .FontColor(Colors.Grey.Darken1);
                });
            });

            var pdfBytes = doc.GeneratePdf();
            return File(pdfBytes, "application/pdf", $"Prescription_{p.PrescriptionId}.pdf");
        }

        // POST: Update prescription via AJAX (only for Doctor)
        [HttpPost]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> UpdatePrescription(int id, [FromForm] Prescription updatedPrescription, IFormFile? file)
        {
            var prescription = await _context.Prescriptions.FindAsync(id);
            if (prescription == null)
                return NotFound();

            prescription.MedicineName = updatedPrescription.MedicineName;
            prescription.Dosage = updatedPrescription.Dosage;
            prescription.Duration = updatedPrescription.Duration;
            prescription.Notes = updatedPrescription.Notes;

            if (file != null && file.Length > 0)
            {
                var uploadDir = Path.Combine("wwwroot", "uploads", "prescriptions");
                if (!Directory.Exists(uploadDir))
                    Directory.CreateDirectory(uploadDir);

                var fileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
                var filePath = Path.Combine(uploadDir, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                prescription.FilePath = $"/uploads/prescriptions/{fileName}";
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> PatientUploads(int appointmentId)
        {
            // Get current doctor ID
            var doctorId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(doctorId))
                return Forbid();

            // Fetch appointment with patient and doctor
            var appointment = await _context.Appointments
                .Include(a => a.Patient)
                    .ThenInclude(p => p.User)
                .Include(a => a.Doctor)
                    .ThenInclude(d => d.User)
                .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);

            if (appointment == null || appointment.Doctor == null || appointment.Patient == null)
                return NotFound();

            // Ensure the logged-in doctor is the owner
            if (appointment.Doctor.UserId != doctorId)  // assuming Doctor entity has UserId
                return Forbid();

            // Get all uploads of the patient
            var uploads = await _context.PatientPrescriptionUploads
                .Where(u => u.PatientId == appointment.Patient.User.Id)
                .OrderByDescending(u => u.UploadDate)
                .ToListAsync();

            ViewBag.PatientName = appointment.Patient.User.FullName;
            ViewBag.AppointmentId = appointment.AppointmentId;

            return View(uploads);
        }



        private bool PrescriptionExists(int id)
        {
            return _context.Prescriptions.Any(e => e.PrescriptionId == id);
        }
    }
}
