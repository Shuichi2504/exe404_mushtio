using System.Net.Http.Json;
using System.Text.Json;

namespace IoTAgriculture.Services;

public class GeminiService
{
    private readonly string _apiKey;
    private readonly string _model;
    private readonly HttpClient _httpClient;

    public GeminiService(IConfiguration configuration, HttpClient httpClient)
    {
        _apiKey = configuration["Gemini:ApiKey"]
            ?? configuration["GeminiApiKey"]
            ?? configuration["GOOGLE_API_KEY"]
            ?? "";
        _model = configuration["Gemini:Model"] ?? "gemini-2.5-flash";
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(45);
    }

    public async Task<string> AskAsync(
        string question,
        IFormFile? image = null,
        string? farmContext = null)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured.");
        }

        var prompt = BuildPrompt(question, farmContext);
        var parts = new List<object>
        {
            new { text = prompt }
        };

        if (image != null && image.Length > 0)
        {
            await using var stream = image.OpenReadStream();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);

            parts.Add(
                new
                {
                    inline_data = new
                    {
                        mime_type = ResolveImageMimeType(image),
                        data = Convert.ToBase64String(memory.ToArray())
                    }
                });
        }

        var request = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts
                }
            }
        };

        var endpoint =
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(_model)}:generateContent?key={Uri.EscapeDataString(_apiKey)}";

        var response = await _httpClient.PostAsJsonAsync(endpoint, request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Gemini request failed: {error}");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(responseStream);
        var root = document.RootElement;

        if (root.TryGetProperty("candidates", out var candidates) &&
            candidates.GetArrayLength() > 0 &&
            candidates[0].TryGetProperty("content", out var content) &&
            content.TryGetProperty("parts", out var responseParts))
        {
            var texts = responseParts
                .EnumerateArray()
                .Where(part => part.TryGetProperty("text", out _))
                .Select(part => part.GetProperty("text").GetString())
                .Where(text => !string.IsNullOrWhiteSpace(text));

            var answer = string.Join(Environment.NewLine, texts);
            if (!string.IsNullOrWhiteSpace(answer))
            {
                return answer;
            }
        }

        return "Không có phản hồi.";
    }

    private static string BuildPrompt(string question, string? farmContext)
    {
        var contextBlock = string.IsNullOrWhiteSpace(farmContext)
            ? "Không có dữ liệu cảm biến hiện tại từ ứng dụng."
            : farmContext.Trim();

        return $"""
Bạn là trợ lý AI chuyên về vận hành trại nấm IoT.

Bạn được phép trả lời các nội dung liên quan đến:
- Điều kiện trại nấm
- Nhiệt độ, độ ẩm không khí, chất lượng không khí
- Tưới nước, thông gió, máy bơm, cảm biến
- Bệnh nấm, dấu hiệu bất thường trên nấm/phôi nấm
- Hình ảnh về nấm, phôi nấm, nhà nấm, môi trường trồng nấm

Khi người dùng hỏi điều kiện trại có lý tưởng hay không, hãy dựa vào dữ liệu cảm biến dưới đây.
Ngưỡng tham khảo:
- Nhiệt độ tốt: 18-30°C
- Độ ẩm không khí tốt: 75-92%

Hãy trả lời bằng tiếng Việt có dấu, ngắn gọn, có kết luận rõ ràng và gợi ý hành động cụ thể.
Nếu thiếu dữ liệu để kết luận, hãy nói rõ dữ liệu nào đang thiếu.
Nếu câu hỏi hoàn toàn ngoài lĩnh vực trại nấm, hãy từ chối lịch sự.

Dữ liệu trại hiện tại:
{contextBlock}

Câu hỏi của người dùng:
{question}
""";
    }

    private static string ResolveImageMimeType(IFormFile image)
    {
        if (!string.IsNullOrWhiteSpace(image.ContentType) &&
            image.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return image.ContentType;
        }

        var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".heic" or ".heif" => "image/heic",
            _ => "image/jpeg"
        };
    }
}
