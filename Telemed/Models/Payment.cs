namespace Telemed.Models
{
    public enum PaymentStatus
    {
        Pending,
        Paid
    }

    // Tracks refund lifecycle (separate from payment status)
    public enum RefundStatus
    {
        None,          // no refund action yet
        Pending,       // refund request created
        Partial,       // partially refunded
        Full,          // fully refunded
        Failed         // Stripe failed or error occurred
    }

    public class Payment
    {
        public int PaymentId { get; set; }

        public int AppointmentId { get; set; }
        public Appointment Appointment { get; set; }

        public decimal Amount { get; set; }

        // Payment lifecycle
        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

        public DateTime? PaymentDate { get; set; }

        // Stripe intent so we can refund correctly
        public string? StripePaymentIntentId { get; set; }

        public int? InvoiceId { get; set; }
        public bool IsInvoiceGenerated { get; set; } = false;

        // ---------------- REFUND FIELDS ----------------

        // Current refund state
        public RefundStatus RefundStatus { get; set; } = RefundStatus.None;

        // Amount refunded so far (supports partial refunds)
        public decimal? RefundAmount { get; set; }

        // When the refund was processed
        public DateTime? RefundDate { get; set; }

        // Stripe refund reference
        public string? StripeRefundId { get; set; }
    }
}
