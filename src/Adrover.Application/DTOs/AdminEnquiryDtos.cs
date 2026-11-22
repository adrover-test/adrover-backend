namespace Adrover.Application.DTOs;

public sealed class AdminEnquiryListItemDto
{
    public Guid EnquiryId { get; set; }
    public string BusinessOrName { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string Requirements { get; set; } = string.Empty;
    public string ReplyStatus { get; set; } = string.Empty;
    public string InternalNotes { get; set; } = string.Empty;
}

public sealed class AdminEnquirySummaryDto
{
    public int TotalEnquiries { get; set; }
    public int AwaitingReply { get; set; }
    public int Replied { get; set; }
}

public sealed class AdminEnquiryListResponseDto
{
    public AdminEnquirySummaryDto Summary { get; set; } = new();
    public List<AdminEnquiryListItemDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}
