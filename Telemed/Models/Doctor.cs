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
        public string BMDCNumber { get; set; }  // ✅ New mandatory field

        public bool IsApproved { get; set; } = false;

        // 💰 New field — consultation fee
        [Range(0, 100000)]
        [DataType(DataType.Currency)]
        [Display(Name = "Consultation Fee (BDT)")]
        public decimal ConsultationFee { get; set; } = 0;

        public ICollection<Appointment> Appointments { get; set; }

        // Helper property to get full name from ApplicationUser
        public string FullName => User != null ? User.FullName ?? "Unknown" : "Unknown";

        // Optional: summary for listing
        public string ShortSummary => Qualification;
    }
}
