namespace IoTAgriculture.Services;

public interface IGeminiService
{
    Task<string> AskAsync(
        string question,
        IFormFile? image = null,
        string? farmContext = null);
}
