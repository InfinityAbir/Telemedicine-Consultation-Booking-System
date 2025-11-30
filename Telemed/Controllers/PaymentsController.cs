using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telemed; // for StripeSettings (if you put StripeSettings.cs in Telemed namespace)
using Telemed.Models;
using Telemed.Payments; // for CreatePaymentDto
using Telemed.Services; // for IInvoiceService, IEmailSenderExtended and TimeZoneHelper
using S = Stripe; // alias Stripe SDK to avoid name collisions with your models

namespace Telemed.Controllers
{
    [Authorize]
    public class PaymentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IInvoiceService _invoiceService;
        private readonly IEmailSenderExtended _emailSender;
        private readonly StripeSettings _stripeSettings;

        public PaymentsController(
            ApplicationDbContext context,
            IInvoiceService invoiceService,
            IEmailSenderExtended emailSender,
            IOptions<StripeSettings> stripeOptions)
        {
            _context = context;
            _invoiceService = invoiceService;
            _emailSender = emailSender;
            _stripeSettings = stripeOptions?.Value ?? new StripeSettings();
            // Ensure StripeConfiguration.ApiKey is set as a fallback (Program.cs should already set it)
            if (!string.IsNullOrWhiteSpace(_stripeSettings.SecretKey))
            {
                S.StripeConfiguration.ApiKey = _stripeSettings.SecretKey;
            }
        }

        // -------------------- LIST PAYMENTS --------------------
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Forbid();

            IQueryable<Payment> paymentsQuery = _context.Payments
                .AsNoTracking()
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Doctor)
                        .ThenInclude(d => d.User)
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Patient)
                        .ThenInclude(pt => pt.User)
                .OrderByDescending(p => p.PaymentDate);

            if (User.IsInRole("Admin"))
            {
                // Admin sees all payments
            }
            else if (User.IsInRole("Doctor"))
            {
                paymentsQuery = paymentsQuery.Where(p => p.Appointment.Doctor.UserId == userId);
            }
            else if (User.IsInRole("Patient"))
            {
                paymentsQuery = paymentsQuery.Where(p => p.Appointment.Patient.UserId == userId);
            }
            else
            {
                return Forbid();
            }

            var payments = await paymentsQuery.ToListAsync();
            return View(payments);
        }

        // -------------------- START PAYMENT (for Patients only) --------------------
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> StartPayment(int appointmentId)
        {
            var payment = await _context.Payments
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Doctor)
                        .ThenInclude(d => d.User)
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Patient)
                        .ThenInclude(pt => pt.User)
                .FirstOrDefaultAsync(p => p.AppointmentId == appointmentId);

            if (payment == null)
            {
                payment = await CreateForAppointment(appointmentId);
            }

            if (payment == null)
            {
                TempData["Error"] = "Unable to start payment for this appointment.";
                return RedirectToAction("Index", "Home");
            }

            // Try to load an invoice for this appointment (if exists)
            var invoice = await _context.Invoices
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.AppointmentId == appointmentId);

            ViewBag.Invoice = invoice; // may be null

            // --- NEW: format scheduled time for display in Dhaka timezone ---
            var appt = payment.Appointment;
            if (appt != null)
            {
                try
                {
                    // Convert stored ScheduledAt (assumed UTC) to Dhaka time for display
                    var scheduledDhaka = TimeZoneHelper.ConvertToDhaka(appt.ScheduledAt);
                    ViewBag.ScheduledDate = scheduledDhaka.ToString("dddd, MMM d, yyyy");
                    ViewBag.ScheduledTime = scheduledDhaka.ToString("hh:mm tt");
                    ViewBag.DoctorName = appt.Doctor?.User?.FullName ?? appt.Doctor?.User?.UserName ?? "Doctor";
                }
                catch
                {
                    // fallback: show raw values if conversion fails
                    ViewBag.ScheduledDate = appt.ScheduledAt.ToString("yyyy-MM-dd");
                    ViewBag.ScheduledTime = appt.ScheduledAt.ToString("HH:mm");
                }
            }

            return View(payment);
        }

        // -------------------- CREATE PAYMENTINTENT FOR CLIENT --------------------
        // Expects a JSON body (CreatePaymentDto). If dto.AppointmentId is present we'll compute the amount server-side.
        [Authorize(Roles = "Patient")]
        [HttpPost]
        [Route("Payments/CreatePaymentIntent")]
        public async Task<IActionResult> CreatePaymentIntent([FromBody] CreatePaymentDto dto)
        {
            if (dto == null) return BadRequest(new { error = "Invalid request body." });

            long amountInCents = 0;
            string currency = string.IsNullOrWhiteSpace(dto.Currency) ? "usd" : dto.Currency;
            string receiptEmail = dto.CustomerEmail ?? string.Empty;
            int appointmentId = dto.AppointmentId;
            Payment? paymentEntity = null;

            if (appointmentId > 0)
            {
                paymentEntity = await _context.Payments
                    .Include(p => p.Appointment)
                        .ThenInclude(a => a.Patient)
                            .ThenInclude(pt => pt.User)
                    .Include(p => p.Appointment)
                        .ThenInclude(a => a.Doctor)
                    .FirstOrDefaultAsync(p => p.AppointmentId == appointmentId);

                if (paymentEntity == null)
                    return BadRequest(new { error = "Payment for appointment not found." });

                amountInCents = (long)Math.Round(paymentEntity.Amount * 100m);
                receiptEmail = paymentEntity.Appointment?.Patient?.User?.Email ?? receiptEmail;
            }
            else if (dto.AmountInCents > 0)
            {
                amountInCents = dto.AmountInCents;
            }
            else
            {
                return BadRequest(new { error = "Amount or AppointmentId required." });
            }

            try
            {
                var service = new Stripe.PaymentIntentService();
                var options = new Stripe.PaymentIntentCreateOptions
                {
                    Amount = amountInCents,
                    Currency = currency,
                    ReceiptEmail = receiptEmail,
                    Metadata = new Dictionary<string, string>
            {
                { "appointment_id", appointmentId.ToString() },
                { "payment_id", paymentEntity?.PaymentId.ToString() ?? "" }
            }
                };

                var intent = await service.CreateAsync(options);

                // Persist the Stripe PaymentIntent id back to your Payment record (if available)
                if (paymentEntity != null)
                {
                    paymentEntity.StripePaymentIntentId = intent.Id;
                    _context.Payments.Update(paymentEntity);
                    await _context.SaveChangesAsync();
                }

                return Ok(new { clientSecret = intent.ClientSecret, id = intent.Id, status = intent.Status });
            }
            catch (Stripe.StripeException sx)
            {
                return BadRequest(new { error = sx.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }


        // -------------------- PROCESS ONLINE PAYMENT --------------------
        [Authorize(Roles = "Patient")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessPayment(int paymentId)
        {
            var payment = await _context.Payments
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Doctor)
                        .ThenInclude(d => d.User)
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Patient)
                        .ThenInclude(pt => pt.User)
                .FirstOrDefaultAsync(p => p.PaymentId == paymentId);

            if (payment == null) return NotFound();

            // mark payment complete
            payment.Status = PaymentStatus.Paid;
            payment.PaymentDate = DateTime.UtcNow;

            if (payment.Appointment != null)
                payment.Appointment.Status = AppointmentStatus.Completed;

            _context.Payments.Update(payment);
            await _context.SaveChangesAsync();

            // ---------- create invoice ----------
            try
            {
                // If an invoice was already generated for this payment, skip creating another one.
                if (payment.IsInvoiceGenerated)
                {
                    TempData["Info"] = "Invoice already generated for this payment.";
                }
                else
                {
                    var appointment = payment.Appointment!;
                    var patient = appointment.Patient;
                    var patientUser = patient?.User;

                    // Pull email and name from the related User entity (Patient doesn't have Email directly)
                    var patientEmail = patientUser?.Email ?? string.Empty;
                    var patientName = patientUser?.FullName ?? "Patient";

                    // Build invoice entity
                    var invoice = new Invoice
                    {
                        AppointmentId = appointment.AppointmentId,
                        PatientId = appointment.PatientId,
                        PatientName = patientName,
                        PatientEmail = patientEmail,
                        LineItems = new List<InvoiceLineItem>
                {
                    new InvoiceLineItem
                    {
                        Description = $"Consultation with Dr. {appointment.Doctor?.User?.FullName ?? "Doctor"}",
                        Quantity = 1,
                        UnitPrice = appointment.Doctor?.ConsultationFee ?? payment.Amount
                    }
                }
                    };

                    invoice.Subtotal = invoice.LineItems.Sum(i => i.LineTotal);
                    invoice.Tax = 0m; // change if you apply tax
                    invoice.Total = invoice.Subtotal + invoice.Tax;

                    // Create and save invoice (this will also generate & write PDF to wwwroot/invoices)
                    invoice = await _invoiceService.CreateAndSaveInvoiceAsync(invoice);

                    // Persist invoice id on the Payment and mark as generated (idempotency guard)
                    payment.InvoiceId = invoice.InvoiceId;
                    payment.IsInvoiceGenerated = true;
                    _context.Payments.Update(payment);
                    await _context.SaveChangesAsync();

                    // Generate PDF bytes for email attachment
                    var pdfBytes = await _invoiceService.GenerateInvoicePdfBytesAsync(invoice);

                    // Build download link for the email (InvoicesController.DownloadInvoice should exist)
                    string downloadLink = Url.Action("DownloadInvoice", "Invoices", new { id = invoice.InvoiceId }, Request.Scheme);

                    // ------------------ Consultation details HTML ------------------
                    string doctorName = appointment.Doctor?.User?.FullName ?? (appointment.Doctor?.User?.UserName ?? "Doctor");
                    // Convert appointment time to Bangladesh timezone
                    var scheduledLocal = TimeZoneHelper.ConvertToDhaka(appointment.ScheduledAt);

                    string scheduledDate = scheduledLocal.ToString("dddd, MMM d, yyyy");
                    string scheduledTime = scheduledLocal.ToString("hh:mm tt");


                    string consultationHtml = $@"
    <table style='border-collapse:collapse; width:100%; max-width:600px; font-family: Arial, Helvetica, sans-serif;'>
        <tr>
            <td style='padding:8px; border:1px solid #e9e9e9; width:170px; font-weight:600;'>Doctor</td>
            <td style='padding:8px; border:1px solid #e9e9e9;'>{System.Net.WebUtility.HtmlEncode(doctorName)}</td>
        </tr>
        <tr>
            <td style='padding:8px; border:1px solid #e9e9e9; font-weight:600;'>Date</td>
            <td style='padding:8px; border:1px solid #e9e9e9;'>{System.Net.WebUtility.HtmlEncode(scheduledDate)}</td>
        </tr>
        <tr>
            <td style='padding:8px; border:1px solid #e9e9e9; font-weight:600;'>Time</td>
            <td style='padding:8px; border:1px solid #e9e9e9;'>{System.Net.WebUtility.HtmlEncode(scheduledTime)}</td>
        </tr>
        <tr>
            <td style='padding:8px; border:1px solid #e9e9e9; font-weight:600;'>Appointment ID</td>
            <td style='padding:8px; border:1px solid #e9e9e9;'>{invoice.AppointmentId}</td>
        </tr>
    </table>
";

                    // Full email HTML body (logo optional)
                    var emailHtml = $@"
    <div style='font-family: Arial, Helvetica, sans-serif; color:#222; font-size:14px;'>
                        <div style='display:flex; align-items:center; gap:10px; margin-bottom:10px;'>
                                <span style=""font-size:28px;"">❤️</span>
                                <h2 style='margin:0; font-size:20px; font-weight:600;'>TeleMed — Consultation Invoice</h2>
                         </div>
        <p>Dear {System.Net.WebUtility.HtmlEncode(invoice.PatientName)},</p>

        <p>Thank you for your payment. Your invoice number is <strong>{invoice.InvoiceNumber}</strong>.</p>

        <p><strong>Consultation details</strong></p>
        {consultationHtml}

        <p>You can download the invoice from <a href='{downloadLink}'>this link</a> or find the attached PDF file.</p>

        <p>If you need help, reply to this email.</p>

        <p>Regards,<br/>Telemed Team</p>
    </div>
";

                    // Send email with attachment — do not crash payment if email fails
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(invoice.PatientEmail))
                            await _emailSender.SendEmailWithAttachmentAsync(invoice.PatientEmail, $"Invoice {invoice.InvoiceNumber}", emailHtml, pdfBytes, $"{invoice.InvoiceNumber}.pdf");
                        else
                            TempData["Info"] = "Invoice created but patient email is missing; cannot send email.";
                    }
                    catch (Exception emailEx)
                    {
                        // log email failure (if you have logger, log it). For now, store a temp message
                        TempData["Info"] = "Payment succeeded but sending invoice email failed.";
                        // Optionally log: Console.WriteLine(emailEx);
                    }
                }
            }
            catch (Exception ex)
            {
                // If invoice generation fails, do not rollback payment — but notify admin or log.
                TempData["Info"] = "Payment processed but invoice generation failed.";
                // Optionally log: Console.WriteLine(ex);
            }

            return RedirectToAction("StartPayment", new { appointmentId = payment.AppointmentId });
        }


        // -------------------- CREATE PAYMENT FOR APPOINTMENT --------------------
        [Authorize(Roles = "Patient")]
        public async Task<Payment?> CreateForAppointment(int appointmentId)
        {
            var appointment = await _context.Appointments
                .Include(a => a.Doctor)
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);

            if (appointment == null)
            {
                // appointment not found
                return null;
            }

            // Prevent creating a payment twice
            var existing = await _context.Payments
                .FirstOrDefaultAsync(p => p.AppointmentId == appointmentId);
            if (existing != null)
            {
                // If an existing payment is already paid, return it (don't create a new one)
                if (existing.Status == PaymentStatus.Paid)
                    return existing;

                // otherwise return the existing pending/other payment
                return existing;
            }

            // Ensure doctor and consultation fee are present
            if (appointment.Doctor == null)
                throw new Exception("Appointment's doctor not found.");

            decimal amount = appointment.Doctor.ConsultationFee;

            var payment = new Payment
            {
                AppointmentId = appointment.AppointmentId,
                Amount = amount,
                Status = PaymentStatus.Pending,
                PaymentDate = null,

                // initialize new fields for Stripe/invoice integration
                StripePaymentIntentId = null,
                InvoiceId = null,
                IsInvoiceGenerated = false
            };

            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();

            // reload with includes for the caller (StartPayment expects includes)
            await _context.Entry(payment).Reference(p => p.Appointment).LoadAsync();
            await _context.Entry(payment.Appointment).Reference(a => a.Doctor).LoadAsync();
            await _context.Entry(payment.Appointment).Reference(a => a.Patient).LoadAsync();

            return payment;
        }


        // -------------------- BOOK APPOINTMENT --------------------
        [Authorize(Roles = "Patient")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BookAppointment(int doctorId, DateTime scheduledAt)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Forbid();

            // Validate doctor exists
            var doctor = await _context.Doctors
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.DoctorId == doctorId);

            if (doctor == null)
            {
                TempData["Error"] = "Selected doctor was not found.";
                return RedirectToAction("Index", "Doctors");
            }

            // Find matching Patient record for current user
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
            if (patient == null)
            {
                TempData["Error"] = "No patient profile found for your account. Please complete your profile before booking an appointment.";
                return RedirectToAction("Profile", "Account");
            }

            // Optional: validate scheduledAt (must be in future)
            // Note: compare in UTC — normalize the incoming value to Local first (assume UI sends local Dhaka time)
            var scheduledLocalAssumed = DateTime.SpecifyKind(scheduledAt, DateTimeKind.Local);
            var scheduledUtc = scheduledLocalAssumed.ToUniversalTime();

            if (scheduledUtc <= DateTime.UtcNow.AddMinutes(-1))
            {
                TempData["Error"] = "Please choose a valid future appointment time.";
                return RedirectToAction("Details", "Doctors", new { id = doctorId });
            }

            var appointment = new Appointment
            {
                DoctorId = doctorId,
                PatientId = patient.PatientId,

                // IMPORTANT: store the appointment in UTC for unambiguous behavior across server/client.
                // We assume the UI supplies local Dhaka time; convert it to UTC here.
                ScheduledAt = scheduledUtc,

                Status = AppointmentStatus.PendingPayment
            };

            _context.Appointments.Add(appointment);
            await _context.SaveChangesAsync();

            // create payment for the appointment
            var payment = await CreateForAppointment(appointment.AppointmentId);
            if (payment == null)
            {
                TempData["Error"] = "An error occurred creating a payment for the appointment.";
                return RedirectToAction("MyAppointments", "Appointments");
            }

            return RedirectToAction("StartPayment", new { appointmentId = appointment.AppointmentId });
        }

        // -------------------- EXPORT CSV --------------------
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ExportCsv()
        {
            var list = await _context.Payments
                .AsNoTracking()
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Doctor)
                        .ThenInclude(d => d.User)
                .Include(p => p.Appointment)
                    .ThenInclude(a => a.Patient)
                        .ThenInclude(pt => pt.User)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("PaymentId,AppointmentId,Doctor,Patient,Amount,Status,PaymentDate");

            foreach (var p in list)
            {
                var docName = p.Appointment?.Doctor?.User?.FullName ?? "";
                var patName = p.Appointment?.Patient?.User?.FullName ?? "";

                docName = docName.Replace("\"", "\"\"");
                patName = patName.Replace("\"", "\"\"");

                var date = p.PaymentDate.HasValue ? p.PaymentDate.Value.ToString("yyyy-MM-dd HH:mm") : "";
                sb.AppendLine($"\"{p.PaymentId}\",\"{p.AppointmentId}\",\"{docName}\",\"{patName}\",{p.Amount},{p.Status},{date}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"payments_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        }

        // -------------------- HELPER --------------------
        private bool PaymentExists(int id)
        {
            return _context.Payments.Any(e => e.PaymentId == id);
        }
    }
}
