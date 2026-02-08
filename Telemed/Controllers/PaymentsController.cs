using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telemed;
using Telemed.Models;
using Telemed.Payments;
using Telemed.Services;
using S = Stripe;

namespace Telemed.Controllers
{
    [Authorize]
    public class PaymentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IInvoiceService _invoiceService;
        private readonly IEmailSenderExtended _emailSender;
        private readonly StripeSettings _stripeSettings;
        private readonly IWebHostEnvironment _env;


        public PaymentsController(
            ApplicationDbContext context,
            IInvoiceService invoiceService,
            IEmailSenderExtended emailSender,
            IOptions<StripeSettings> stripeOptions,
            IWebHostEnvironment env)
        {
            _context = context;
            _invoiceService = invoiceService;
            _emailSender = emailSender;
            _env = env;
            _stripeSettings = stripeOptions?.Value ?? new StripeSettings();

            if (!string.IsNullOrWhiteSpace(_stripeSettings.SecretKey))
                S.StripeConfiguration.ApiKey = _stripeSettings.SecretKey;
        }


        // --------------------------------------------------
        // LIST PAYMENTS
        // --------------------------------------------------
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

            if (User.IsInRole("Doctor"))
                paymentsQuery = paymentsQuery.Where(p => p.Appointment.Doctor.UserId == userId);
            else if (User.IsInRole("Patient"))
                paymentsQuery = paymentsQuery.Where(p => p.Appointment.Patient.UserId == userId);

            var payments = await paymentsQuery.ToListAsync();
            return View(payments);
        }

        // --------------------------------------------------
        // START PAYMENT
        // --------------------------------------------------
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
                payment = await CreateForAppointment(appointmentId);

            if (payment == null)
            {
                TempData["Error"] = "Unable to start payment for this appointment.";
                return RedirectToAction("Index", "Home");
            }

            var invoice = await _context.Invoices
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.AppointmentId == appointmentId);

            ViewBag.Invoice = invoice;

            var appt = payment.Appointment;
            if (appt != null)
            {
                try
                {
                    var scheduledDhaka = TimeZoneHelper.ConvertToDhaka(appt.ScheduledAt);
                    ViewBag.ScheduledDate = scheduledDhaka.ToString("dddd, MMM d, yyyy");
                    ViewBag.ScheduledTime = scheduledDhaka.ToString("hh:mm tt");
                    ViewBag.DoctorName = appt.Doctor?.User?.FullName ?? "Doctor";
                }
                catch
                {
                    ViewBag.ScheduledDate = appt.ScheduledAt.ToString("yyyy-MM-dd");
                    ViewBag.ScheduledTime = appt.ScheduledAt.ToString("HH:mm");
                }
            }

            return View(payment);
        }

        // --------------------------------------------------
        // CREATE PAYMENT INTENT
        // --------------------------------------------------
        [Authorize(Roles = "Patient")]
        [HttpPost]
        [Route("Payments/CreatePaymentIntent")]
        public async Task<IActionResult> CreatePaymentIntent([FromBody] CreatePaymentDto dto)
        {
            if (dto == null)
                return BadRequest(new { error = "Invalid request body." });

            long amountInCents = 0;
            string currency = string.IsNullOrWhiteSpace(dto.Currency) ? "usd" : dto.Currency;
            string receiptEmail = dto.CustomerEmail ?? string.Empty;

            int appointmentId = dto.AppointmentId;
            Payment? paymentEntity = null;

            if (appointmentId > 0)
            {
                paymentEntity = await _context.Payments
                    .Include(p => p.Appointment).ThenInclude(a => a.Patient).ThenInclude(pt => pt.User)
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
                var intent = await service.CreateAsync(new Stripe.PaymentIntentCreateOptions
                {
                    Amount = amountInCents,
                    Currency = currency,
                    ReceiptEmail = receiptEmail,
                    Metadata = new Dictionary<string, string>
                    {
                        { "appointment_id", appointmentId.ToString() },
                        { "payment_id", paymentEntity?.PaymentId.ToString() ?? "" }
                    }
                });

                if (paymentEntity != null)
                {
                    paymentEntity.StripePaymentIntentId = intent.Id;
                    _context.Payments.Update(paymentEntity);
                    await _context.SaveChangesAsync();
                }

                return Ok(new { clientSecret = intent.ClientSecret, id = intent.Id, status = intent.Status });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // --------------------------------------------------
        // PROCESS PAYMENT SUCCESS
        // --------------------------------------------------
        [Authorize(Roles = "Patient")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessPayment(int paymentId)
        {
            var payment = await _context.Payments
                .Include(p => p.Appointment).ThenInclude(a => a.Patient).ThenInclude(pt => pt.User)
                .Include(p => p.Appointment).ThenInclude(a => a.Doctor).ThenInclude(d => d.User)
                .FirstOrDefaultAsync(p => p.PaymentId == paymentId);

            if (payment == null)
                return NotFound();

            payment.Status = PaymentStatus.Paid;
            payment.PaymentDate = DateTime.UtcNow;

            if (payment.Appointment != null)
            {
                var appt = payment.Appointment;
                appt.Status = AppointmentStatus.Approved;
                appt.Amount = payment.Amount;
                appt.PaymentStatus = "Paid";
                appt.TransactionId = payment.StripePaymentIntentId ?? payment.PaymentId.ToString();
            }

            _context.Payments.Update(payment);
            await _context.SaveChangesAsync();

            // --------------------------------------------------
            // INVOICE CREATION + EMAIL
            // --------------------------------------------------
            try
            {
                var appointment = payment.Appointment;

                var invoice = new Invoice
                {
                    AppointmentId = appointment.AppointmentId,
                    PatientId = appointment.PatientId,
                    PatientName = appointment.Patient.User.FullName,
                    PatientEmail = appointment.Patient.User.Email,
                    Subtotal = payment.Amount,
                    Tax = 0,
                    Total = payment.Amount,
                    LineItems = new List<InvoiceLineItem>
            {
                new InvoiceLineItem
                {
                    Description = "Teleconsultation Fee",
                    Quantity = 1,
                    UnitPrice = payment.Amount
                }
            }
                };

                // create invoice + save PDF
                invoice = await _invoiceService.CreateAndSaveInvoiceAsync(invoice);

                payment.InvoiceId = invoice.InvoiceId;
                payment.IsInvoiceGenerated = true;

                await _context.SaveChangesAsync();

                // read PDF file
                var filePath = Path.Combine(
                    _env.WebRootPath,
                    invoice.PdfFilePath.TrimStart('/')
                );

                var pdfBytes = await System.IO.File.ReadAllBytesAsync(filePath);

                // send email with attachment
                var downloadUrl = Url.Action(
     "DownloadInvoice",
     "Invoices",
     new { id = invoice.InvoiceId },
     protocol: Request.Scheme
 );
                var local = TimeZoneHelper.ConvertToDhaka(payment.Appointment.ScheduledAt);

                var htmlBody = $@"
<h2>TeleMed — Consultation Invoice</h2>

<p>Dear {invoice.PatientName},</p>

<p>Thank you for your payment. Your invoice number is <strong>{invoice.InvoiceNumber}</strong>.</p>

<h3>Consultation details</h3>

<table style='border-collapse:collapse'>
<tr><td><strong>Doctor</strong></td><td>{payment.Appointment.Doctor.User.FullName}</td></tr>
<tr><td><strong>Date</strong></td><td>{local:dddd, MMM d, yyyy}</td></tr>
<tr><td><strong>Time</strong></td><td>{local:hh:mm tt}</td></tr>
<tr><td><strong>Appointment ID</strong></td><td>{payment.AppointmentId}</td></tr>
</table>

<p>You can download the invoice from <a href='{downloadUrl}'>this link</a> or find the attached PDF.</p>

<p>Regards,<br/>TeleMed Team</p>
";


                await _emailSender.SendEmailWithAttachmentAsync(
                    invoice.PatientEmail,
                    "TeleMed — Consultation Invoice",
                    htmlBody,
                    pdfBytes,
                    $"{invoice.InvoiceNumber}.pdf"
                );

            }
            catch (Exception ex)
            {
                // don't break the payment flow
                Console.WriteLine("Invoice or email error: " + ex.Message);
            }

            return RedirectToAction("StartPayment", new { appointmentId = payment.AppointmentId });
        }


        // --------------------------------------------------
        // REFUND API  (AUTO + ADMIN)
        // --------------------------------------------------

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> Refund(int paymentId, decimal amount)
        {
            var payment = await _context.Payments
                .Include(p => p.Appointment)
                .FirstOrDefaultAsync(p => p.PaymentId == paymentId);

            if (payment == null)
                return NotFound();
            // prevent double refunds
            if (payment.RefundStatus == RefundStatus.Full || payment.RefundStatus == RefundStatus.Partial)
            {
                TempData["Error"] = "Refund already processed for this payment.";
                return RedirectToAction("Index");
            }

            if (payment.Status != PaymentStatus.Paid)
            {
                TempData["Error"] = "Payment is not paid. Cannot refund.";
                return RedirectToAction("Index");
            }

            if (string.IsNullOrWhiteSpace(payment.StripePaymentIntentId))
            {
                TempData["Error"] = "Stripe PaymentIntent not found. Cannot refund.";
                return RedirectToAction("Index");
            }

            try
            {
                var refundService = new Stripe.RefundService();

                var refund = await refundService.CreateAsync(new Stripe.RefundCreateOptions
                {
                    PaymentIntent = payment.StripePaymentIntentId,
                    Amount = (long)Math.Round(amount * 100m)
                });

                // track refund metadata
                payment.RefundAmount = amount;
                payment.RefundDate = DateTime.UtcNow;
                payment.StripeRefundId = refund.Id;

                // choose status correctly
                if (amount == payment.Amount)
                    payment.RefundStatus = RefundStatus.Full;
                else if (amount > 0)
                    payment.RefundStatus = RefundStatus.Partial;
                else
                    payment.RefundStatus = RefundStatus.None;

                // appointment state
                payment.Appointment.Status = AppointmentStatus.RefundCompleted;

                await _context.SaveChangesAsync();

                //
                // EMAIL NOTIFICATION TO PATIENT
                //
                try
                {
                    var patientEmail = payment.Appointment?.Patient?.User?.Email;
                    var patientName = payment.Appointment?.Patient?.User?.FullName ?? "Patient";

                    if (!string.IsNullOrWhiteSpace(patientEmail))
                    {
                        var subject = "TeleMed — Refund Processed";

                        var htmlBody = $@"
            <h2>Refund completed</h2>

            <p>Hi {patientName},</p>

            <p>Your refund for Appointment ID 
               <strong>{payment.AppointmentId}</strong> has been processed.</p>

            <p><strong>Refund Amount:</strong> {payment.RefundAmount}</p>
            <p><strong>Status:</strong> {payment.RefundStatus}</p>

            <p>The money will appear in your account depending on your bank or card provider.</p>

            <p>Regards,<br/>TeleMed Support</p>
        ";

                        await _emailSender.SendEmailWithAttachmentAsync(
                            patientEmail,
                            subject,
                            htmlBody,
                            null,
                            null
                        );
                    }
                }
                catch (Exception emailEx)
                {
                    // Don't break refund flow if email fails
                    Console.WriteLine("Refund email send failed: " + emailEx.Message);
                }

                TempData["Success"] = "Refund processed successfully.";

            }
            catch (Exception ex)
            {
                payment.RefundStatus = RefundStatus.Failed;
                await _context.SaveChangesAsync();

                TempData["Error"] = "Refund failed: " + ex.Message;
            }
            return RedirectToAction("Index");
        }

        // --------------------------------------------------
        // CREATE PAYMENT FOR APPOINTMENT
        // --------------------------------------------------
        [Authorize(Roles = "Patient")]
        public async Task<Payment?> CreateForAppointment(int appointmentId)
        {
            var appointment = await _context.Appointments
                .Include(a => a.Doctor)
                .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);

            if (appointment == null)
                return null;

            var existing = await _context.Payments.FirstOrDefaultAsync(p => p.AppointmentId == appointmentId);
            if (existing != null)
                return existing;

            var payment = new Payment
            {
                AppointmentId = appointment.AppointmentId,
                Amount = appointment.Doctor.ConsultationFee,
                Status = PaymentStatus.Pending,
                StripePaymentIntentId = null,
                InvoiceId = null,
                IsInvoiceGenerated = false
            };

            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();

            return payment;
        }

        // --------------------------------------------------
        // EXPORT CSV
        // --------------------------------------------------
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ExportCsv()
        {
            var list = await _context.Payments
                .AsNoTracking()
                .Include(p => p.Appointment)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("PaymentId,AppointmentId,Amount,Status,PaymentDate");

            foreach (var p in list)
            {
                var date = p.PaymentDate.HasValue ? p.PaymentDate.Value.ToString("yyyy-MM-dd HH:mm") : "";
                sb.AppendLine($"{p.PaymentId},{p.AppointmentId},{p.Amount},{p.Status},{date}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"payments_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        }
    }
}
