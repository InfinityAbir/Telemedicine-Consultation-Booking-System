namespace Telemed.Models
{
    public class Patient
    {
        public int PatientId { get; set; }
        public string UserId { get; set; }
        public ApplicationUser User { get; set; }

        public DateTime? DOB { get; set; }
        public string Gender { get; set; }
        public string ContactNumber { get; set; }

        public ICollection<Appointment> Appointments { get; set; }
    }
}
