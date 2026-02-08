namespace Telemed.Payments
{
    public class CreatePaymentDto
    {
        public int AppointmentId { get; set; }    // id of appointment or order
        public long AmountInCents { get; set; }   // amount in cents (e.g., 5000 for $50.00)
        public string Currency { get; set; } = "usd";
        public string CustomerEmail { get; set; } // optional
    }
}
