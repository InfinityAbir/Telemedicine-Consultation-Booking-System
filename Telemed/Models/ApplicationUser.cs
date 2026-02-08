using Microsoft.AspNetCore.Identity;
using System;
using System.ComponentModel.DataAnnotations;

namespace Telemed.Models
{
    public enum IdType
    {
        NID = 0,
        Passport = 1
    }

    public class ApplicationUser : IdentityUser
    {
        [StringLength(100)]
        public string? FullName { get; set; }

        [StringLength(10)]
        public string? Gender { get; set; }

        [DataType(DataType.Date)]
        public DateTime? DateOfBirth { get; set; }

        [StringLength(1000)]
        public string? Address { get; set; }

        [StringLength(20)]
        public string? UserRole { get; set; } // Admin / Doctor / Patient

        [StringLength(100)]
        public string? ProfileImageUrl { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;

        // ---------------- NEW FIELDS ----------------

        [Required]
        public IdType IdType { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "NID / Passport Number")]
        public string IdNumber { get; set; } = string.Empty;
    }
}
