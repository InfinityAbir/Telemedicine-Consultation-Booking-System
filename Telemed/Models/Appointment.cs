namespace Telemed.Models
{
    // Appointment lifecycle ONLY (no money stuff here)
    public enum AppointmentStatus
    {
        PendingPayment,
        AwaitingDoctorApproval,
        Approved,
        Rejected,
        Rescheduled,
        Completed,

        // Cancellations
        CancelledByPatient,
        CancelledByDoctor,
        RefundPending,      // 👈 needed
        RefundCompleted
    }

    public class Appointment
    {
        public int AppointmentId { get; set; }

        public int DoctorId { get; set; }
        public Doctor Doctor { get; set; }

        public int PatientId { get; set; }
        public Patient Patient { get; set; }

        public DateTime ScheduledAt { get; set; }

        // Default state: waiting for payment
        public AppointmentStatus Status { get; set; } = AppointmentStatus.PendingPayment;

        public string? PatientNote { get; set; }
        public string? DoctorNote { get; set; }

        // Optional linking to doctor schedule
        public int? ScheduleId { get; set; }
        public DoctorSchedule? Schedule { get; set; }

        public int AppointmentOrder { get; set; }

        // Money info (still useful for reports)
        public decimal Amount { get; set; }

        public string? TransactionId { get; set; }

        // "Pending", "Paid", "Failed"
        public string? PaymentStatus { get; set; }
        public string? VideoCallLink { get; set; }
    }
}
