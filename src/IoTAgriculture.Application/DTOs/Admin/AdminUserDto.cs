namespace IoTAgriculture.DTOs.Admin
{
    public class AdminUserDto
    {
        public Guid UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Role { get; set; } = "user";
        public string AccountType { get; set; } = "standard";
        public DateTime? LastActiveAt { get; set; }
        public DateTime? DeactivatedAt { get; set; }
        public bool IsActive => DeactivatedAt == null;
        public bool IsOnline =>
            IsActive &&
            LastActiveAt.HasValue &&
            DateTime.UtcNow - LastActiveAt.Value <= TimeSpan.FromMinutes(5);
    }
}
