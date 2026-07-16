using System.ComponentModel.DataAnnotations;

namespace IoTAgriculture.DTOs.Auth
{
    public class RegisterRequestDto
    {
        [Required, MaxLength(120)]
        public string FullName { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(120)]
        public string Email { get; set; } = string.Empty;

        [Required, MaxLength(6), MinLength(6)]
        public string EmailVerificationCode { get; set; } = string.Empty;

        [Required, MaxLength(255)]
        public string Address { get; set; } = string.Empty;

        [Required]
        public DateTime DateOfBirth { get; set; }

        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;
    }
}
