namespace IoTAgriculture.Services;

/// <summary>
/// Shared environmental assessment thresholds for grey oyster mushroom
/// (Pleurotus sajor-caju) fruiting rooms.
/// </summary>
public static class MushroomEnvironmentThresholds
{
    public const double LowTemperatureCelsius = 16;
    public const double HighTemperatureWarningCelsius = 30;
    public const double HighTemperatureCriticalCelsius = 35;

    public const double LowHumidityPercent = 80;
    public const double HighHumidityPercent = 95;
}
