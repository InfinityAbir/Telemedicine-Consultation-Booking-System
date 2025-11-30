using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Telemed.ViewModels
{
    public class RegisterViewModel : IValidatableObject
    {
        [Required]
        [Display(Name = "Full name")]
        [StringLength(100, ErrorMessage = "Full name cannot exceed 100 characters.")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least {2} characters.")]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Register as")]
        public string RegisterAs { get; set; } = "Patient";

        // Patient fields
        [DataType(DataType.Date)]
        [Display(Name = "Date of birth")]
        public DateTime? DOB { get; set; }

        [Display(Name = "Gender")]
        [StringLength(20, ErrorMessage = "Gender cannot exceed 20 characters.")]
        public string? Gender { get; set; }

        [Phone]
        [Display(Name = "Contact number")]
        [StringLength(20, ErrorMessage = "Contact number cannot exceed 20 characters.")]
        public string? ContactNumber { get; set; }

        // Doctor fields
        [Display(Name = "BM&DC Registration Number")]
        [StringLength(50, ErrorMessage = "BM&DC number cannot exceed 50 characters.")]
        public string? BMDCNumber { get; set; }

        [Display(Name = "Specialization")]
        [StringLength(150, ErrorMessage = "Specialization cannot exceed 150 characters.")]
        public string? Specialization { get; set; }

        [Display(Name = "Qualification")]
        [StringLength(250, ErrorMessage = "Qualification cannot exceed 250 characters.")]
        public string? Qualification { get; set; }

        [Display(Name = "Consultation Fee (BDT)")]
        [Range(0, 100000, ErrorMessage = "Consultation fee must be between 0 and 100,000.")]
        public decimal? ConsultationFee { get; set; } = 0;

        [Display(Name = "Short bio / clinic address")]
        [StringLength(1000, ErrorMessage = "Bio / clinic address cannot exceed 1000 characters.")]
        public string? Bio { get; set; }

        // Conditional validation
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (RegisterAs == "Doctor" && string.IsNullOrWhiteSpace(BMDCNumber))
            {
                yield return new ValidationResult(
                    "BM&DC Registration Number is required for doctors.",
                    new[] { nameof(BMDCNumber) });
            }
        }
    }
}
