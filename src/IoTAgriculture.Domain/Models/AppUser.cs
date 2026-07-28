using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTAgriculture.Models
{
    [Table("Users")]
    public class AppUser
    {
        [Key]
        public Guid UserId { get; set; }

        [Required, MaxLength(120)]
        public string FullName { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        [MaxLength(120)]
        public string Email { get; set; } = string.Empty;

        public bool EmailVerified { get; set; }

        [Required, MaxLength(255)]
        public string Address { get; set; } = string.Empty;

        public DateTime DateOfBirth { get; set; }

        public int Role { get; set; }

        [Required, MaxLength(255)]
        public string PasswordHash { get; set; } = string.Empty;

        [Required, MaxLength(256)]
        public string PasswordSalt { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? DeactivatedAt { get; set; }

        public DateTime? LastActiveAt { get; set; }

        [Required, MaxLength(20)]
        public string AccountType { get; set; } = "standard";
    }
}
