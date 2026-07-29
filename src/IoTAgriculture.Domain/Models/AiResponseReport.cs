using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTAgriculture.Models
{
    [Table("AiResponseReports")]
    public class AiResponseReport
    {
        [Key]
        public Guid AiResponseReportId { get; set; }

        public Guid UserId { get; set; }

        [Required, MaxLength(80)]
        public string Reason { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Note { get; set; }

        [Required, MaxLength(4000)]
        public string Prompt { get; set; } = string.Empty;

        [Required, MaxLength(12000)]
        public string Response { get; set; } = string.Empty;

        [Required, MaxLength(32)]
        public string Status { get; set; } = "pending";

        public DateTime CreatedAt { get; set; }

        public AppUser? User { get; set; }
    }
}
