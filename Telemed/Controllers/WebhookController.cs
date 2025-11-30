using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telemed.Services;
using Telemed.Models;
using S = Stripe;      // Stripe SDK alias
using M = Telemed.Models; // alias for your models

namespace Telemed.Controllers
{
    [ApiController]
    [Route("webhook")]
    public class WebhookController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IInvoiceService _invoiceService;
        private readonly IEmailSenderExtended _emailSender;
        private readonly M.StripeSettings _stripeSettings;
        private readonly ILogger<WebhookController> _logger;

        public WebhookController(
            ApplicationDbContext context,
            IInvoiceService invoiceService,
            IEmailSenderExtended emailSender,
            IOptions<M.StripeSettings> stripeOptions,
            ILogger<WebhookController> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _invoiceService = invoiceService ?? throw new ArgumentNullException(nameof(invoiceService));
            _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
            _stripeSettings = stripeOptions?.Value ?? new M.StripeSettings();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpPost]
        public async Task<IActionResult> Post()
        {
            // Read raw request body
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

            var webhookSecret = _stripeSettings?.WebhookSecret ?? string.Empty;
            S.Event stripeEvent;

            try
            {
                var signatureHeader = Request.Headers["Stripe-Signature"];
                stripeEvent = S.EventUtility.ConstructEvent(json, signatureHeader, webhookSecret);
            }
            catch (Exception ex)
            {
                _logger.LogError("Webhook signature verification failed: {Message}", ex.Message);
                return BadRequest();
            }

            _logger.LogInformation("Received Stripe event: {Type}", stripeEvent.Type);

            // Use the literal event name to avoid SDK-version differences
            if (string.Equals(stripeEvent.Type, "payment_intent.succeeded", StringComparison.OrdinalIgnoreCase))
            {
                var pi = stripeEvent.Data.Object as S.PaymentIntent;
                if (pi == null)
                {
                    _logger.LogWarning("PaymentIntent object missing in event data.");
                    return Ok();
                }

                // Try to find Payment by stored StripePaymentIntentId first
                M.Payment? payment = null;
                if (!string.IsNullOrEmpty(pi.Id))
                {
                    payment = await _context.Payments.FirstOrDefaultAsync(p => p.StripePaymentIntentId == pi.Id);
                }

                // Fallback: check metadata appointment_id
                if (payment == null && pi.Metadata != null && pi.Metadata.ContainsKey("appointment_id"))
                {
                    if (int.TryParse(pi.Metadata["appointment_id"], out var apptId))
                    {
                        payment = await _context.Payments.FirstOrDefaultAsync(p => p.AppointmentId == apptId);
                    }
                }

                if (payment == null)
                {
                    _logger.LogWarning("Payment record not found for PaymentIntent: {IntentId}", pi.Id);
                    // Return 200 so Stripe won't keep retrying for a mapping that doesn't exist
                    return Ok();
                }

                try
                {
                    // Idempotent: only process if not already marked Paid
                    if (payment.Status != M.PaymentStatus.Paid)
                    {
                        payment.Status = M.PaymentStatus.Paid;
                        payment.PaymentDate = DateTime.UtcNow;

                        var appt = await _context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == payment.AppointmentId);
                        if (appt != null)
                        {
                            appt.Status = M.AppointmentStatus.Completed;
                        }

                        _context.Payments.Update(payment);
                        await _context.SaveChangesAsync();

                        _logger.LogInformation("Payment {PaymentId} marked as Paid", payment.PaymentId);

                        // Generate invoice and email (same behavior as your ProcessPayment logic)
                        try
                        {
                            var appointment = await _context.Appointments
                                .Include(a => a.Doctor).ThenInclude(d => d.User)
                                .Include(a => a.Patient).ThenInclude(pt => pt.User)
                                .FirstOrDefaultAsync(a => a.AppointmentId == payment.AppointmentId);

                            if (appointment != null)
                            {
                                var patientUser = appointment.Patient?.User;
                                var patientEmail = patientUser?.Email ?? string.Empty;
                                var patientName = patientUser?.FullName ?? "Patient";

                                var invoice = new M.Invoice
                                {
                                    AppointmentId = appointment.AppointmentId,
                                    PatientId = appointment.PatientId,
                                    PatientName = patientName,
                                    PatientEmail = patientEmail,
                                    LineItems = new List<M.InvoiceLineItem>
                                    {
                                        new M.InvoiceLineItem
                                        {
                                            Description = $"Consultation with Dr. {appointment.Doctor?.User?.FullName ?? "Doctor"}",
                                            Quantity = 1,
                                            UnitPrice = appointment.Doctor?.ConsultationFee ?? payment.Amount
                                        }
                                    }
                                };

                                invoice.Subtotal = invoice.LineItems.Sum(i => i.LineTotal);
                                invoice.Tax = 0m;
                                invoice.Total = invoice.Subtotal + invoice.Tax;

                                invoice = await _invoice_service_CreateAndSaveAsync(invoice); // helper below
                                var pdfBytes = await _invoiceService.GenerateInvoicePdfBytesAsync(invoice);

                                // Build download link for email (InvoicesController.DownloadInvoice should exist)
                                string downloadLink = Url.Action("DownloadInvoice", "Invoices", new { id = invoice.InvoiceId }, Request.Scheme);

                                string emailHtml = $@"
                                    <div>
                                        <p>Dear {System.Net.WebUtility.HtmlEncode(invoice.PatientName)},</p>
                                        <p>Thank you for your payment. Your invoice number is <strong>{invoice.InvoiceNumber}</strong>.</p>
                                        <p><a href='{downloadLink}'>Download invoice</a></p>
                                    </div>
                                ";

                                if (!string.IsNullOrWhiteSpace(invoice.PatientEmail))
                                {
                                    await _emailSender.SendEmailWithAttachmentAsync(invoice.PatientEmail, $"Invoice {invoice.InvoiceNumber}", emailHtml, pdfBytes, $"{invoice.InvoiceNumber}.pdf");
                                }
                            }
                        }
                        catch (Exception invEx)
                        {
                            _logger.LogError(invEx, "Failed to generate/send invoice after webhook payment confirmation.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing payment_intent.succeeded webhook.");
                }
            }
            else
            {
                _logger.LogInformation("Unhandled event type {Type}", stripeEvent.Type);
            }

            return Ok();
        }

        // Small helper to call your invoice service (keeps main code tidy)
        private async Task<M.Invoice> _invoice_service_CreateAndSaveAsync(M.Invoice invoice)
        {
            return await _invoiceService.CreateAndSaveInvoiceAsync(invoice);
        }
    }
}
