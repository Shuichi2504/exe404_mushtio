namespace IoTAgriculture.Models
{
    public static class AccountTypes
    {
        public const string Standard = "standard";
        public const string Premium = "premium";

        public static bool IsValid(string? value)
        {
            return string.Equals(value, Standard, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, Premium, StringComparison.OrdinalIgnoreCase);
        }

        public static string Normalize(string? value)
        {
            return string.Equals(value, Premium, StringComparison.OrdinalIgnoreCase)
                ? Premium
                : Standard;
        }
    }
}
