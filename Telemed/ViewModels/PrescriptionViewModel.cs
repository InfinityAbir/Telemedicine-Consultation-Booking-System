using System;
using System.ComponentModel.DataAnnotations;

namespace Telemed.ViewModels
{
    /// <summary>
    /// Model used by doctors to create a prescription for an appointment.
    /// </summary>
    public class PrescriptionViewModel
    {
        [Required]
        [Display(Name = "Appointment Id")]
        public int AppointmentId { get; set; }

        [Required]
        [Display(Name = "Medicine / Drug name")]
        [StringLength(250)]
        public string MedicineName { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Dosage / Instructions")]
        [StringLength(500)]
        public string Dosage { get; set; } = string.Empty;

        [Display(Name = "Duration (e.g., 5 days, 2 weeks)")]
        [StringLength(100)]
        public string? Duration { get; set; }

        [Display(Name = "Additional notes")]
        [StringLength(1000)]
        public string? Notes { get; set; }

        [Display(Name = "Created at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
