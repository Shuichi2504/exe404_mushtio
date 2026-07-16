using Microsoft.AspNetCore.Http;

public class ChatRequestDto
{
    public string Message { get; set; } = "";

    public Guid UserId { get; set; }

    public IFormFile? Image { get; set; }
}
