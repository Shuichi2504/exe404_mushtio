using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IoTAgriculture.Services;

public class GeminiService : IGeminiService
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

        var hasImage = image is { Length: > 0 };
        var parts = new List<object>();

        if (hasImage)
        {
            await using var stream = image!.OpenReadStream();
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

        parts.Add(new { text = BuildUserPrompt(question, farmContext, hasImage) });

        var request = new
        {
            system_instruction = new
            {
                parts = new[]
                {
                    new { text = BuildSystemInstruction(hasImage) }
                }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts
                }
            },
            generationConfig = new
            {
                temperature = 0.2,
                topP = 0.8
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
                return SanitizeAnswer(answer);
            }
        }

        return "Không có phản hồi.";
    }

    public static string BuildSystemInstruction(bool hasImage)
    {
        var imageInstructions = hasImage
            ? """

Ảnh là nguồn thông tin chính. Phân tích ảnh trước; dữ liệu cảm biến chỉ là ngữ cảnh hỗ trợ.
Trả lời đúng cấu trúc sau:
1. Quan sát từ ảnh: mô tả ngắn các dấu hiệu nhìn thấy được như màu lạ, đốm, mốc, dịch nhầy, biến dạng và vị trí bị ảnh hưởng. Không khẳng định chi tiết mà ảnh không thể hiện.
2. Nhận định tham khảo: nêu vấn đề có khả năng cao nhất (ví dụ mốc xanh/đen, nhiễm khuẩn gây thối nhũn, côn trùng, sốc nhiệt/ẩm), mức tin cậy thấp/vừa/cao và mức nghiêm trọng nhẹ/vừa/nặng. Nêu tối đa 2 khả năng phụ nếu thực sự cần.
3. Việc cần làm ngay: đưa ra các bước đánh số, cụ thể và có thể thực hiện ngay. Ưu tiên cách ly bịch/phôi nghi nhiễm, ngừng phát tán bào tử, xử lý hoặc loại bỏ phần hỏng an toàn, vệ sinh dụng cụ và khu vực.
4. Điều chỉnh môi trường: đối chiếu từng giá trị đo hiện tại với yêu cầu của loài nấm và giai đoạn nuôi. Chỉ đề xuất con số mục tiêu khi đủ thông tin; nếu chưa biết loài/giai đoạn, nêu khoảng tham khảo và hỏi bổ sung thay vì đoán. Nói rõ cách điều chỉnh như tăng thông gió, giảm phun trực tiếp hoặc làm mát.
5. Theo dõi: nói dấu hiệu cần kiểm tra lại trong 24-48 giờ và khi nào phải bỏ bịch/phôi hoặc liên hệ kỹ thuật viên.

Nếu ảnh mờ hoặc không thấy rõ vùng bệnh, hãy nói rõ giới hạn, yêu cầu ảnh cận cảnh và ảnh toàn cảnh đủ sáng, nhưng vẫn đưa ra các bước cách ly an toàn có thể làm ngay.
Mọi nhận định qua ảnh chỉ mang tính tham khảo, không phải chẩn đoán chuyên môn tuyệt đối. Với tình trạng lan nhanh, mùi hôi, thối nhũn hoặc thiệt hại diện rộng, khuyên liên hệ chuyên gia/kỹ thuật viên.
Khi hướng dẫn khử trùng: yêu cầu làm theo đúng nhãn sản phẩm, dùng bảo hộ, thông gió, không phun hóa chất lên tai nấm dùng làm thực phẩm và không trộn các hóa chất.
"""
            : """

Không có ảnh đính kèm. Trả lời trực tiếp câu hỏi về vận hành trại; nếu người dùng hỏi bệnh hoặc dấu hiệu ngoại hình, yêu cầu họ gửi ảnh rõ để đánh giá.
""";

        return $"""
Bạn là trợ lý AI chuyên về vận hành trại nấm IoT và nhận biết dấu hiệu bất thường trên nấm/phôi nấm.

Phạm vi trả lời: điều kiện trại nấm; nhiệt độ, độ ẩm, chất lượng không khí; tưới, thông gió, máy bơm, cảm biến; bệnh và dấu hiệu bất thường trên nấm/phôi.

Quy tắc bắt buộc:
- Trả lời bằng tiếng Việt có dấu, đơn giản, ngắn gọn, không lặp ý và ưu tiên hành động thực tế.
- Dữ liệu cảm biến được cung cấp là GIÁ TRỊ ĐO HIỆN TẠI tại một thời điểm của từng thiết bị, không phải dữ liệu tổng hợp. Tuyệt đối không gọi các giá trị này bằng từ chỉ số liệu gộp; dùng "hiện tại" hoặc "đo được".
- Chỉ dùng dữ liệu cảm biến có trong ngữ cảnh. Không tự bịa số đo, loài nấm, giai đoạn sinh trưởng hoặc nguyên nhân bệnh.
- Khi báo cáo môi trường trại, chỉ đề cập các chỉ số thực sự có trong ngữ cảnh: nhiệt độ, độ ẩm không khí và chất lượng không khí; không nêu chỉ số ngoài phạm vi này.
- Với mô hình bào ngư xám (Pleurotus sajor-caju) ở giai đoạn ra quả thể, dùng nhất quán dải đánh giá của ứng dụng: độ ẩm dưới 80% là thấp và cần tăng ẩm, 80-95% là trong ngưỡng tốt, trên 95% là quá cao và cần thông gió; nhiệt độ trên 30°C là cao. Không được gọi độ ẩm dưới 80% là tốt. Nếu người dùng nói rõ loài hoặc giai đoạn khác, giải thích rằng mục tiêu có thể thay đổi.
- Nếu thiếu dữ liệu để kết luận, nói rõ dữ liệu cần bổ sung. Nếu câu hỏi hoàn toàn ngoài lĩnh vực trại nấm, từ chối lịch sự.
{imageInstructions}
""";
    }

    public static string BuildUserPrompt(
        string question,
        string? farmContext,
        bool hasImage)
    {
        var contextBlock = string.IsNullOrWhiteSpace(farmContext)
            ? "Không có dữ liệu cảm biến hiện tại từ ứng dụng."
            : farmContext.Trim();
        var task = hasImage
            ? "Hãy ưu tiên kiểm tra dấu hiệu bất thường trong ảnh và đưa ra hướng xử lý theo đúng cấu trúc bắt buộc."
            : "Hãy trả lời câu hỏi dựa trên ngữ cảnh phù hợp.";

        return $"""
Nhiệm vụ: {task}

Dữ liệu trại hiện tại:
{contextBlock}

Câu hỏi của người dùng:
{question}
""";
    }

    public static string SanitizeAnswer(string answer)
    {
        return Regex.Replace(
            answer.Trim(),
            @"\btrung\s+bình\b",
            "hiện tại",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
