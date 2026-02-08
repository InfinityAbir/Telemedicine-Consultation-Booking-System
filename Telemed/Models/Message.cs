namespace Telemed.Models
{
    public class Message
    {
        public int MessageId { get; set; }
        public int? AppointmentId { get; set; }     // optional link to appointment
        public Appointment Appointment { get; set; }

        public string SenderId { get; set; }
        public ApplicationUser Sender { get; set; }

        public string ReceiverId { get; set; }
        public ApplicationUser Receiver { get; set; }

        public string Content { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
