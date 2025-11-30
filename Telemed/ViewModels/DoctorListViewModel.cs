using System.ComponentModel.DataAnnotations;

namespace Telemed.ViewModels
{
    /// <summary>
    /// Lightweight model for listing doctors on the booking/search page.
    /// </summary>
    public class DoctorListViewModel
    {
        [Display(Name = "Doctor Id")]
        public int DoctorId { get; set; }

        [Display(Name = "User Id")]
        public string? UserId { get; set; }

        [Display(Name = "Name")]
        public string FullName { get; set; } = string.Empty;

        [Display(Name = "Specialization")]
        public string Specialization { get; set; } = string.Empty;

        [Display(Name = "Qualification")]
        public string? Qualification { get; set; }

        [Display(Name = "Is Approved")]
        public bool IsApproved { get; set; }

        // Optional short description for list view
        [Display(Name = "Summary")]
        public string? ShortSummary { get; set; }

        // Example helper for UI: display label for approval state
        public string ApprovalLabel => IsApproved ? "Approved" : "Pending";
    }
}
