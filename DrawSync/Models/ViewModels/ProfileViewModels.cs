using System.ComponentModel.DataAnnotations;

namespace DrawSync.Models.ViewModels
{
    public class ProfileViewModel
    {
        public string Name { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string RoleName { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
    }

    public class EditProfileViewModel
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = null!;

        [DataType(DataType.Password)]
        [Display(Name = "Current Password")]
        public string? CurrentPassword { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} characters long.", MinimumLength = 6)]
        public string? NewPassword { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm New Password")]
        [Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
        public string? ConfirmPassword { get; set; }
    }
}
