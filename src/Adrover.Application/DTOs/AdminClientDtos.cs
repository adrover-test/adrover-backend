using Adrover.Application.DTOs.Advertisement;

namespace Adrover.Application.DTOs;

public sealed class AdminClientListItemDto
{
    public Guid ClientId { get; set; }
    public string ClientOrBusiness { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int Advertisements { get; set; }
    public List<AdvertisementDto> Campaigns { get; set; } = new();
}

public sealed class AdminClientListResponseDto
{
    public List<AdminClientListItemDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}
