using System;
using System.ComponentModel.DataAnnotations;

namespace Telemed.Models
{
    public class DoctorScheduleMultiDayViewModel
    {
        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Start Date")]
        public DateTime StartDate { get; set; }

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "End Date")]
        public DateTime EndDate { get; set; }

        [Required]
        [DataType(DataType.Time)]
        [Display(Name = "Start Time")]
        public TimeSpan StartTime { get; set; }  // TimeSpan instead of string

        [Required]
        [DataType(DataType.Time)]
        [Display(Name = "End Time")]
        public TimeSpan EndTime { get; set; }    // TimeSpan instead of string

        [Required]
        [Range(1, 100)]
        [Display(Name = "Max Patients Per Day")]
        public int MaxPatientsPerDay { get; set; }

        [Url]
        [Display(Name = "Video Call Link")]
        public string? VideoCallLink { get; set; }
    }
}
