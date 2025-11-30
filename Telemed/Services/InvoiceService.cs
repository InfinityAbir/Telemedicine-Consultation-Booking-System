using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Telemed.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Telemed.Services
{
    public class InvoiceService : IInvoiceService
    {
        private readonly IWebHostEnvironment _env;
        private readonly ApplicationDbContext _db;

        public InvoiceService(IWebHostEnvironment env, ApplicationDbContext db)
        {
            _env = env;
            _db = db;
        }

        public async Task<Invoice> CreateAndSaveInvoiceAsync(Invoice invoice)
        {
            invoice.IssuedAt = DateTime.UtcNow;
            invoice.InvoiceNumber = GenerateInvoiceNumber();

            // Save metadata to DB
            _db.Invoices.Add(invoice);
            await _db.SaveChangesAsync();

            // Generate PDF bytes
            var bytes = await GenerateInvoicePdfBytesAsync(invoice);

            // Ensure folder
            var invoicesFolder = Path.Combine(_env.WebRootPath, "invoices");
            if (!Directory.Exists(invoicesFolder))
                Directory.CreateDirectory(invoicesFolder);

            var fileName = $"{invoice.InvoiceNumber}.pdf";
            var path = Path.Combine(invoicesFolder, fileName);

            // Write file (atomic-ish: write bytes)
            await File.WriteAllBytesAsync(path, bytes);

            invoice.PdfFilePath = $"/invoices/{fileName}";
            _db.Invoices.Update(invoice);
            await _db.SaveChangesAsync();

            return invoice;
        }

        public async Task<byte[]> GenerateInvoicePdfBytesAsync(Invoice invoice)
        {
            // Local helper for formatting currency with Taka symbol
            static string FormatTaka(decimal amount)
            {
                return "৳" + amount.ToString("N2", CultureInfo.InvariantCulture);
            }

            // Try to load appointment and doctor details for inclusion in PDF header
            Appointment? appointment = null;
            string doctorName = "Doctor";
            DateTime? scheduledAt = null;

            if (invoice.AppointmentId != 0)
            {
                appointment = await _db.Appointments
                    .Where(a => a.AppointmentId == invoice.AppointmentId)
                    .Include(a => a.Doctor)
                        .ThenInclude(d => d.User)
                    .Include(a => a.Patient)
                        .ThenInclude(p => p.User)
                    .FirstOrDefaultAsync();

                if (appointment != null)
                {
                    doctorName = appointment.Doctor?.User?.FullName
                                 ?? appointment.Doctor?.User?.UserName
                                 ?? "Doctor";
                    scheduledAt = appointment.ScheduledAt;
                }
            }

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontSize(12));

                    // Header now includes consultation details when available
                    page.Header().Element(header =>
                    {
                        header.Column(col =>
                        {
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Column(left =>
                                {
                                    left.Item().Text($"Telemed").SemiBold().FontSize(16);
                                    left.Item().Text($"Invoice - {invoice.InvoiceNumber}").FontSize(12);
                                });

                                row.ConstantItem(180).Column(right =>
                                {
                                    right.Item().Text($"Date: {invoice.IssuedAt:yyyy-MM-dd}");
                                    right.Item().Text($"Invoice #: {invoice.InvoiceNumber}");
                                });
                            });

                            if (scheduledAt.HasValue)
                            {
                                // Convert to Dhaka timezone safely
                                var local = ConvertToDhaka(scheduledAt.Value);

                                // Use explicit ToString to avoid interpolation format pitfalls
                                col.Item().PaddingTop(6).Row(r2 =>
                                {
                                    r2.RelativeItem().Text($"Patient: {invoice.PatientName}");
                                    r2.RelativeItem().Text($"Doctor: {doctorName}");
                                    r2.RelativeItem().Text($"Scheduled: {local.ToString("yyyy-MM-dd")} {local.ToString("hh:mm tt")}");
                                });
                            }
                            else
                            {
                                col.Item().PaddingTop(6).Text($"Patient: {invoice.PatientName}");
                            }
                        });
                    });

                    page.Content().Element(c =>
                    {
                        c.Column(col =>
                        {
                            col.Item().PaddingTop(10).Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(6);
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(2);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Text("Description").Bold();
                                    header.Cell().Text("Qty").Bold();
                                    header.Cell().AlignRight().Text("Unit Price").Bold();
                                    header.Cell().AlignRight().Text("Line Total").Bold();
                                });

                                foreach (var item in invoice.LineItems ?? Enumerable.Empty<InvoiceLineItem>())
                                {
                                    table.Cell().Text(item.Description);
                                    table.Cell().Text(item.Quantity.ToString());
                                    table.Cell().AlignRight().Text(FormatTaka(item.UnitPrice));
                                    table.Cell().AlignRight().Text(FormatTaka(item.Quantity * item.UnitPrice));
                                }

                                table.Footer(footer =>
                                {
                                    footer.Cell().ColumnSpan(2).AlignLeft().Text("");
                                    footer.Cell().AlignRight().Text("Subtotal:");
                                    footer.Cell().AlignRight().Text(FormatTaka(invoice.Subtotal));

                                    footer.Cell().ColumnSpan(2).AlignLeft().Text("");
                                    footer.Cell().AlignRight().Text("Tax:");
                                    footer.Cell().AlignRight().Text(FormatTaka(invoice.Tax));

                                    footer.Cell().ColumnSpan(2).AlignLeft().Text("");
                                    footer.Cell().AlignRight().Text("Total:").SemiBold();
                                    footer.Cell().AlignRight().Text(FormatTaka(invoice.Total)).SemiBold();
                                });
                            });

                            col.Item().PaddingTop(20).Text("Thank you for your payment.");
                        });
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Telemed System - ");
                        x.Span($"Generated on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
                    });
                });
            });

            // QuestPDF generates bytes synchronously; wrap in Task for async signature
            var bytes = doc.GeneratePdf();
            return await Task.FromResult(bytes);
        }

        private static DateTime ConvertToDhaka(DateTime dt)
        {
            try
            {
                // Get Dhaka timezone
                var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");

                // Determine UTC instant from incoming DateTime depending on Kind
                DateTime utc;
                if (dt.Kind == DateTimeKind.Utc)
                {
                    utc = dt;
                }
                else if (dt.Kind == DateTimeKind.Local)
                {
                    utc = dt.ToUniversalTime();
                }
                else
                {
                    // Unspecified: assume stored as UTC. If your app stored local times, change this.
                    utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                }

                // Convert from UTC to Dhaka
                return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
            }
            catch
            {
                // Fallback: if timezone not found or conversion fails, return original value
                return dt;
            }
        }

        private string GenerateInvoiceNumber()
        {
            return $"INV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString().Split('-')[0].ToUpper()}";
        }
    }
}
