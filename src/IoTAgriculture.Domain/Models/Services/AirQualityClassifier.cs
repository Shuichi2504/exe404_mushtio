namespace IoTAgriculture.Services;

public static class AirQualityClassifier
{
    public const string Unit = "ppm";
    public const double GoodUpperBoundPpm = 800;
    public const double AcceptableUpperBoundPpm = 1000;
    public const double SlightlyHighUpperBoundPpm = 1500;
    public const double HighUpperBoundPpm = 2000;

    // Push notifications start above 1,500 ppm. Change this policy here only.
    public const double PushAlertThresholdPpm = SlightlyHighUpperBoundPpm;

    public static AirQualityClassification Classify(double? value)
    {
        if (value == null)
        {
            return AirQualityClassification.NoData;
        }

        if (value <= GoodUpperBoundPpm)
        {
            return new("Tốt", "normal", false, "info", null);
        }

        if (value <= AcceptableUpperBoundPpm)
        {
            return new("Chấp nhận được", "acceptable", false, "info", null);
        }

        if (value <= SlightlyHighUpperBoundPpm)
        {
            return new("Khá cao", "warning", false, "info", null);
        }

        if (value <= HighUpperBoundPpm)
        {
            return new("Cao", "danger", true, "warning", TimeSpan.FromMinutes(5));
        }

        return new(
            "Rất cao, nên thông gió",
            "critical",
            true,
            "critical",
            TimeSpan.FromMinutes(1));
    }
}

public sealed record AirQualityClassification(
    string Label,
    string Level,
    bool ShouldAlert,
    string Severity,
    TimeSpan? RepeatInterval)
{
    public static readonly AirQualityClassification NoData =
        new("Chưa có dữ liệu", "muted", false, "info", null);
}
