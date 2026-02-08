using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Telemed.Models
{
    public class Doctor
    {
        [Key]
        public int DoctorId { get; set; }

        // Link to the ApplicationUser
        [Required]
        public string UserId { get; set; }
        public ApplicationUser User { get; set; }

        [Required]
        [StringLength(100)]
        public string Specialization { get; set; }

        [StringLength(200)]
        public string Qualification { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "BM&DC Registration Number")]
        public string BMDCNumber { get; set; }

        // Approval status (controlled by admin)
        public bool IsApproved { get; set; } = false;

        // 💰 Live consultation fee (visible to patients)
        [Range(0, 100000)]
        [DataType(DataType.Currency)]
        [Display(Name = "Consultation Fee (BDT)")]
        public decimal ConsultationFee { get; set; } = 0;

        // ⏳ Pending fee (requested by doctor, not yet approved)
        [Range(0, 100000)]
        [Display(Name = "Pending Consultation Fee")]
        public decimal? PendingConsultationFee { get; set; }

        public ICollection<Appointment> Appointments { get; set; }

        // Helper property
        public string FullName => User?.FullName ?? "Unknown";

        public string ShortSummary => Qualification;
    }
}
