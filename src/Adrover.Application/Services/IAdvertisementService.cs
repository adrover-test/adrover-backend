using Adrover.Application.DTOs;
using Adrover.Application.DTOs.Advertisement;

namespace Adrover.Application.Services;

/// <summary>
/// Interface for advertisement management service
/// </summary>
public interface IAdvertisementService
{
    Task<AdvertisementDto> GetAdvertisementAsync(Guid id);
    Task<AdvertisementListResponse> GetClientAdvertisementsAsync(Guid clientId, int pageNumber = 1, int pageSize = 10);
    Task<AdvertisementListResponse> GetPendingApprovalsAsync(int pageNumber = 1, int pageSize = 10);
    Task<AdvertisementDto> CreateAdvertisementAsync(Guid clientId, CreateAdvertisementRequest request);
    Task<AdvertisementDto> UpdateAdvertisementAsync(Guid id, Guid clientId, UpdateAdvertisementRequest request);
    Task DeleteAdvertisementAsync(Guid id, Guid clientId);
    Task<AdvertisementDto> SubmitForApprovalAsync(Guid id, Guid clientId);
    Task<AdvertisementDto> ApproveAdvertisementAsync(Guid id, string adminName);
    Task<AdvertisementDto> RejectAdvertisementAsync(Guid id, string rejectionReason, string adminName);
    Task<AdvertisementAnalyticsDto> GetAnalyticsAsync(Guid id);
    Task<FileUploadResponse> UploadVideoAsync(Guid advertisementId, Guid clientId, Stream fileStream, string fileName);
    Task<FileUploadResponse> UploadImageAsync(Guid advertisementId, Guid clientId, Stream fileStream, string fileName);
}
