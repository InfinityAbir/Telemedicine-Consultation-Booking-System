namespace Telemed.Models
{
    public class Prescription
    {
        public int PrescriptionId { get; set; }
        public int AppointmentId { get; set; }
        public Appointment Appointment { get; set; }

        public string MedicineName { get; set; }
        public string Dosage { get; set; }
        public string Duration { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Notes { get; set; }
        public string? FilePath { get; set; }

    }
}
