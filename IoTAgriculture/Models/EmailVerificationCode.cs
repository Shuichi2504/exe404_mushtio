using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTAgriculture.Models
{
    [Table("EmailVerificationCodes")]
    public class EmailVerificationCode
    {
        [Key]
        public Guid VerificationId { get; set; }

        [Required, MaxLength(120)]
        public string Email { get; set; } = string.Empty;

        [Required, MaxLength(6)]
        public string Code { get; set; } = string.Empty;

        [Required, MaxLength(40)]
        public string Purpose { get; set; } = string.Empty;

        public Guid? UserId { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime ExpiresAt { get; set; }

        public DateTime? VerifiedAt { get; set; }

        public DateTime? UsedAt { get; set; }
    }
}
