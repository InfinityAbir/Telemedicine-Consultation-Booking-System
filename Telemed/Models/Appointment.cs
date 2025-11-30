namespace Telemed.Models
{
    public enum AppointmentStatus
    {
        PendingPayment,        // created, awaiting payment
        AwaitingDoctorApproval, // paid, waiting for doctor approval
        Approved,
        Rejected,
        Rescheduled,
        Completed
    }


    public class Appointment
    {
        public int AppointmentId { get; set; }
        public int DoctorId { get; set; }
        public Doctor Doctor { get; set; }

        public int PatientId { get; set; }
        public Patient Patient { get; set; }

        public DateTime ScheduledAt { get; set; }
        public AppointmentStatus Status { get; set; } = AppointmentStatus.PendingPayment;

        public string? PatientNote { get; set; }
        public string? DoctorNote { get; set; }

        // Optional linking
        public int? ScheduleId { get; set; }
        public DoctorSchedule? Schedule { get; set; }
        public int AppointmentOrder { get; set; }

        // 💰 New fields
        public decimal Amount { get; set; }
        public string? TransactionId { get; set; }
        public string? PaymentStatus { get; set; } // "Pending", "Paid", "Failed"
    }

}
