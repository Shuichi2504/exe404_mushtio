using System.ComponentModel.DataAnnotations;

namespace IoTAgriculture.API.Contracts
{
    public class RegisterFcmTokenRequest
    {
        [Required, MaxLength(512)]
        public string FcmToken { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Platform { get; set; } = string.Empty;
    }
}
