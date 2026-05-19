using System.ComponentModel.DataAnnotations;

namespace DrawSync.Models.ViewModels
{
    public class ForgotPasswordViewModel
    {
        [Required(ErrorMessage = "Email address is required")]
        [EmailAddress(ErrorMessage = "Invalid email address format")]
        [Display(Name = "Email Address")]
        public string Email { get; set; } = null!;
    }

    public class ResetPasswordViewModel
    {
        [Required(ErrorMessage = "Password is required")]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        public string Password { get; set; } = null!;

        [Required(ErrorMessage = "Password confirmation is required")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = null!;
    }

    public class CompleteGoogleSignupViewModel
    {
        [Required(ErrorMessage = "Organization name is required")]
        [StringLength(100, ErrorMessage = "The organization name must be at least {2} characters long.", MinimumLength = 2)]
        [Display(Name = "Organization Name")]
        public string OrganizationName { get; set; } = null!;

        public string? Email { get; set; }
        public string? Name { get; set; }
    }
}
