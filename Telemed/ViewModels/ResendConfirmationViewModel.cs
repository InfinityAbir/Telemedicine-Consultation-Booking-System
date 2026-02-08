using System.ComponentModel.DataAnnotations;

namespace Telemed.ViewModels
{
    public class ResendConfirmationViewModel
    {
        [Required(ErrorMessage = "Please enter your email.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;
    }
}
