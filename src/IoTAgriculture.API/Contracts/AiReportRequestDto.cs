namespace IoTAgriculture.API.Contracts;

public class AiReportRequestDto
{
    public Guid UserId { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string? Note { get; set; }

    public string Prompt { get; set; } = string.Empty;

    public string Response { get; set; } = string.Empty;
}
