using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Telemed.Models
{
    public class Invoice
    {
        public int InvoiceId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public DateTime IssuedAt { get; set; }

        public int AppointmentId { get; set; }
        public Appointment? Appointment { get; set; }

        public int PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public string PatientEmail { get; set; } = string.Empty;

        public decimal Subtotal { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }

        // PDF path stored relative to wwwroot, e.g. "/invoices/INV-...pdf"
        public string? PdfFilePath { get; set; }

        // Keep as navigational property so EF can persist line items if desired
        public List<InvoiceLineItem> LineItems { get; set; } = new();
    }

    public class InvoiceLineItem
    {
        public int InvoiceLineItemId { get; set; }
        public int InvoiceId { get; set; }
        public Invoice? Invoice { get; set; }

        public string Description { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; }
        [NotMapped]
        public decimal LineTotal => Quantity * UnitPrice;
    }
}
