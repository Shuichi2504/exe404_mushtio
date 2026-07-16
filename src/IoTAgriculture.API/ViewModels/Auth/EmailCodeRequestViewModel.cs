using System.ComponentModel.DataAnnotations;

namespace IoTAgriculture.ViewModels.Auth
{
    public class EmailCodeRequestViewModel
    {
        [Required, EmailAddress, MaxLength(120)]
        public string Email { get; set; } = string.Empty;

        [Required, MaxLength(40)]
        public string Purpose { get; set; } = "register";
    }
}
