namespace Telemed.Models
{
    public enum PaymentStatus
    {
        Pending,
        Paid
    }

    public class Payment
    {
        public int PaymentId { get; set; }

        public int AppointmentId { get; set; }
        public Appointment Appointment { get; set; }

        public decimal Amount { get; set; }

        // Status of the payment
        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

        public DateTime? PaymentDate { get; set; }

        // 🔹 NEW: Stores Stripe PaymentIntent ID for webhook verification and reconciliation
        public string? StripePaymentIntentId { get; set; }
        public int? InvoiceId { get; set; }              // FK to Invoices table (nullable)
        public bool IsInvoiceGenerated { get; set; } = false;
    }
}
