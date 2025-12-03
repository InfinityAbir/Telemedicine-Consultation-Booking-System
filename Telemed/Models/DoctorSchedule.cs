using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Telemed.Models
{
    public class DoctorSchedule
    {
        [Key]
        public int ScheduleId { get; set; }

        [Required]
        public int DoctorId { get; set; }
        public Doctor Doctor { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime Date { get; set; }

        [Required]
        [DataType(DataType.Time)]
        public TimeSpan StartTime { get; set; }

        [Required]
        [DataType(DataType.Time)]
        public TimeSpan EndTime { get; set; }

        [Required]
        [Range(1, 100, ErrorMessage = "Number of patients must be between 1 and 100.")]
        public int MaxPatientsPerDay { get; set; }

        [Url]
        public string VideoCallLink { get; set; }

        // ✅ No admin approval needed: schedules are active immediately
        public bool IsApproved { get; set; } = true;

        [NotMapped]
        public double SlotDurationMinutes => (EndTime - StartTime).TotalMinutes / MaxPatientsPerDay;
    }
}
