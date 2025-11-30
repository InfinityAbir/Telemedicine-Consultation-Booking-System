using Telemed.Models;

namespace Telemed.ViewModels
{
    public class AppointmentWithFeedbackStatus
    {
        public Appointment Appointment { get; set; } = default!;
        public bool HasFeedback { get; set; }

        // populated by the controller (may be null if no payment record)
        public Payment? Payment { get; set; }

        // convenience property used by the view to check payment state
        public bool IsPaid => Payment != null && Payment.Status == PaymentStatus.Paid;
    }
}
