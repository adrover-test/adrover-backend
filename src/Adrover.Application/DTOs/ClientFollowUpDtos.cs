using System.ComponentModel.DataAnnotations;

namespace Adrover.Application.DTOs;

public sealed class ClientFollowUpRequest
{
    public Guid RequestId { get; set; }
    [Required, RegularExpression("^(note|email|sms)$")]
    public string Channel { get; set; } = "note";
    [Required(AllowEmptyStrings = true), MaxLength(200)]
    public string Subject { get; set; } = string.Empty;
    [Required, MaxLength(2000)]
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset? NextFollowUpAt { get; set; }
}

public sealed class ClientFollowUpDto
{
    public Guid Id { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
    public string By { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTimeOffset? NextFollowUpAt { get; set; }
}
