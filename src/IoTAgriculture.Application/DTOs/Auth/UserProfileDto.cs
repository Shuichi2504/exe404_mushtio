namespace IoTAgriculture.DTOs.Auth
{
    public class UserProfileDto
    {
        public Guid UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public DateTime DateOfBirth { get; set; }
        public string Role { get; set; } = "user";
        public string AccountType { get; set; } = "standard";
    }
}
