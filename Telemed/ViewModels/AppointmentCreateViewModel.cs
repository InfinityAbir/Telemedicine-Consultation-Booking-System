using System;
using System.ComponentModel.DataAnnotations;

namespace Telemed.ViewModels
{
    /// <summary>
    /// Model used when a patient creates a new appointment.
    /// ScheduledAt uses the HTML datetime-local control format in Razor views.
    /// </summary>
    public class AppointmentCreateViewModel
    {
        [Required]
        [Display(Name = "Doctor")]
        public int DoctorId { get; set; }

        // Optional display name of the doctor shown on the booking form
        [Display(Name = "Doctor name")]
        public string? DoctorName { get; set; }

        [Required]
        [Display(Name = "Appointment date & time")]
        // Use input type="datetime-local" in the view. Bind as DateTime (local).
        public DateTime ScheduledAt { get; set; }

        [Display(Name = "Short note (reason for visit)")]
        [StringLength(1000)]
        public string? PatientNote { get; set; }

        // Optional: estimated duration or preferred slot
        [Display(Name = "Preferred duration (minutes)")]
        public int? PreferredDurationMinutes { get; set; }

        // Optional: if you want to render available slot options on the form
        public string? AvailableSlotsJson { get; set; }
    }
}
