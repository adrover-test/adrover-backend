using Adrover.Application.DTOs;
using Adrover.Application.DTOs.Advertisement;
using Adrover.Application.Exceptions;
using Adrover.Domain.Entities;
using Adrover.Data.Repositories;
using Adrover.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace Adrover.Application.Services;

/// <summary>
/// Service for managing advertisement campaigns
/// </summary>
public class AdvertisementService : IAdvertisementService
{
    private readonly IRepository<Advertisement> _advertisementRepository;
    private readonly IRepository<AdPlay> _adPlayRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<AdvertisementService> _logger;

    private const long MaxVideoFileSize = 100 * 1024 * 1024; // 100MB
    private const long MaxImageFileSize = 10 * 1024 * 1024; // 10MB
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 2000;

    public AdvertisementService(
        IRepository<Advertisement> advertisementRepository,
        IRepository<AdPlay> adPlayRepository,
        IRepository<User> userRepository,
        IFileStorageService fileStorageService,
        ILogger<AdvertisementService> logger)
    {
        _advertisementRepository = advertisementRepository;
        _adPlayRepository = adPlayRepository;
        _userRepository = userRepository;
        _fileStorageService = fileStorageService;
        _logger = logger;
    }

    public async Task<AdvertisementDto> GetAdvertisementAsync(Guid id)
    {
        var ad = await _advertisementRepository.GetByIdAsync(id);
        if (ad == null)
            throw new NotFoundException($"Advertisement with ID {id} not found");

        return MapToAdvertisementDto(ad);
    }

    public async Task<AdvertisementListResponse> GetClientAdvertisementsAsync(Guid clientId, int pageNumber = 1, int pageSize = 10)
    {
        if (pageNumber < 1 || pageSize < 1 || pageSize > 100)
            throw new Application.Exceptions.ValidationException("Invalid pagination parameters");

        var advertisements = await _advertisementRepository.FindAsync(ad => ad.ClientId == clientId);
        var orderedAds = advertisements.OrderByDescending(a => a.SubmittedAt).ToList();
        
        var total = orderedAds.Count;
        var skip = (pageNumber - 1) * pageSize;
        var pagedItems = orderedAds.Skip(skip).Take(pageSize).ToList();

        var items = pagedItems.Select(MapToAdvertisementDto).ToList();

        return new AdvertisementListResponse
        {
            Total = total,
            Skip = skip,
            Take = pageSize,
            Items = items
        };
    }

    public async Task<AdvertisementListResponse> GetPendingApprovalsAsync(int pageNumber = 1, int pageSize = 10)
    {
        if (pageNumber < 1 || pageSize < 1 || pageSize > 100)
            throw new Application.Exceptions.ValidationException("Invalid pagination parameters");

        var advertisements = await _advertisementRepository.FindAsync(ad => ad.Status == AdvertisementStatus.PendingApproval);
        var orderedAds = advertisements.OrderBy(a => a.SubmittedAt).ToList();
        
        var total = orderedAds.Count;
        var skip = (pageNumber - 1) * pageSize;
        var pagedItems = orderedAds.Skip(skip).Take(pageSize).ToList();

        var items = pagedItems.Select(MapToAdvertisementDto).ToList();

        return new AdvertisementListResponse
        {
            Total = total,
            Skip = skip,
            Take = pageSize,
            Items = items
        };
    }

    public async Task<AdvertisementDto> CreateAdvertisementAsync(Guid clientId, CreateAdvertisementRequest request)
    {
        var client = await _userRepository.GetByIdAsync(clientId);
        if (client == null)
            throw new NotFoundException("Client user not found");

        if (client.Role != UserRole.Client)
            throw new AuthorizationException("Only clients can create advertisements");

        var advertisement = new Advertisement
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            Name = request.Name,
            Brand = request.Brand,
            Description = request.Description,
            TargetArea = request.TargetArea,
            VideoDurationSeconds = request.VideoDurationSeconds,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            TargetPlays = request.TargetPlays,
            AssignedCabs = request.AssignedCabs,
            ApprovalAmount = request.ApprovalAmount,
            Status = AdvertisementStatus.PendingApproval,
            SubmittedAt = DateTime.UtcNow,
            ActualPlays = 0,
            PaidAmount = 0
        };

        await _advertisementRepository.AddAsync(advertisement);

        _logger.LogInformation("Advertisement created: {AdvertisementId} by client {ClientId}", advertisement.Id, clientId);

        return MapToAdvertisementDto(advertisement);
    }

    public async Task<AdvertisementDto> UpdateAdvertisementAsync(Guid id, Guid clientId, UpdateAdvertisementRequest request)
    {
        var advertisement = await _advertisementRepository.GetByIdAsync(id);
        if (advertisement == null)
            throw new NotFoundException($"Advertisement with ID {id} not found");

        if (advertisement.ClientId != clientId)
            throw new AuthorizationException("You can only update your own advertisements");

        if (advertisement.Status != AdvertisementStatus.PendingApproval)
            throw new Application.Exceptions.ValidationException("Can only update advertisements in pending approval status");

        advertisement.Name = request.Name;
        advertisement.Brand = request.Brand;
        advertisement.Description = request.Description;
        advertisement.TargetArea = request.TargetArea;
        advertisement.VideoDurationSeconds = request.VideoDurationSeconds;
        advertisement.StartDate = request.StartDate;
        advertisement.EndDate = request.EndDate;
        advertisement.TargetPlays = request.TargetPlays;
        advertisement.AssignedCabs = request.AssignedCabs;
        advertisement.ApprovalAmount = request.ApprovalAmount;
        advertisement.UpdatedAt = DateTime.UtcNow;

        await _advertisementRepository.UpdateAsync(advertisement);

        _logger.LogInformation("Advertisement updated: {AdvertisementId}", id);

        return MapToAdvertisementDto(advertisement);
    }

    public async Task DeleteAdvertisementAsync(Guid id, Guid clientId)
    {
        var advertisement = await _advertisementRepository.GetByIdAsync(id);
        if (advertisement == null)
            throw new NotFoundException($"Advertisement with ID {id} not found");

        if (advertisement.ClientId != clientId)
            throw new AuthorizationException("You can only delete your own advertisements");

        if (advertisement.Status != AdvertisementStatus.PendingApproval && advertisement.Status != AdvertisementStatus.Rejected)
            throw new Application.Exceptions.ValidationException("Can only delete advertisements in pending or rejected status");

        await _advertisementRepository.DeleteAsync(advertisement);

        _logger.LogInformation("Advertisement deleted: {AdvertisementId}", id);
    }

    public async Task<AdvertisementDto> SubmitForApprovalAsync(Guid id, Guid clientId)
    {
        var advertisement = await _advertisementRepository.GetByIdAsync(id);
        if (advertisement == null)
            throw new NotFoundException($"Advertisement with ID {id} not found");

        if (advertisement.ClientId != clientId)
            throw new AuthorizationException("You can only submit your own advertisements");

        if (advertisement.Status != AdvertisementStatus.PendingApproval)
            throw new Application.Exceptions.ValidationException("Advertisement is not in pending status");

        if (string.IsNullOrEmpty(advertisement.VideoUrl) && string.IsNullOrEmpty(advertisement.ImageUrl))
            throw new Application.Exceptions.ValidationException("Advertisement must have at least one video or image");

        advertisement.Status = AdvertisementStatus.Approved;
        advertisement.UpdatedAt = DateTime.UtcNow;

        await _advertisementRepository.UpdateAsync(advertisement);

        _logger.LogInformation("Advertisement submitted for approval: {AdvertisementId}", id);

        return MapToAdvertisementDto(advertisement);
    }

    public async Task<AdvertisementDto> ApproveAdvertisementAsync(Guid id, string adminName)
    {
        var advertisement = await _advertisementRepository.GetByIdAsync(id);
        if (advertisement == null)
            throw new NotFoundException($"Advertisement with ID {id} not found");

        if (advertisement.Status != AdvertisementStatus.Approved)
            throw new Application.Exceptions.ValidationException("Only approved advertisements can be activated");

        advertisement.Status = AdvertisementStatus.Active;
        advertisement.ApprovedBy = adminName;
        advertisement.ApprovedAt = DateTime.UtcNow;
        advertisement.UpdatedAt = DateTime.UtcNow;

        await _advertisementRepository.UpdateAsync(advertisement);

        _logger.LogInformation("Advertisement approved: {AdvertisementId} by {AdminName}", id, adminName);

        return MapToAdvertisementDto(advertisement);
    }

    public async Task<AdvertisementDto> RejectAdvertisementAsync(Guid id, string rejectionReason, string adminName)
    {
        var advertisement = await _advertisementRepository.GetByIdAsync(id);
        if (advertisement == null)
            throw new NotFoundException($"Advertisement with ID {id} not found");

        if (advertisement.Status != AdvertisementStatus.Approved)
            throw new Application.Exceptions.ValidationException("Only approved advertisements can be rejected");

        advertisement.Status = AdvertisementStatus.Rejected;
        advertisement.RejectionReason = rejectionReason;
        advertisement.ApprovedBy = adminName;
        advertisement.RejectedAt = DateTime.UtcNow;
        advertisement.UpdatedAt = DateTime.UtcNow;

        await _advertisementRepository.UpdateAsync(advertisement);

        _logger.LogInformation("Advertisement rejected: {AdvertisementId} by {AdminName}", id, adminName);

        return MapToAdvertisementDto(advertisement);
    }

    public async Task<AdvertisementAnalyticsDto> GetAnalyticsAsync(Guid id)
    {
        var advertisement = await _advertisementRepository.GetByIdAsync(id);
        if (advertisement == null)
            throw new NotFoundException($"Advertisement with ID {id} not found");

        var adPlays = await _adPlayRepository.FindAsync(ap => ap.AdvertisementId == id);
        var actualPlays = adPlays.Count();

        var daysActive = advertisement.Status == AdvertisementStatus.Active
            ? (DateTime.UtcNow - advertisement.StartDate).Days
            : (advertisement.EndDate - advertisement.StartDate).Days;

        daysActive = Math.Max(1, daysActive);

        var playPercentage = advertisement.TargetPlays > 0
            ? (decimal)actualPlays / advertisement.TargetPlays * 100
            : 0;

        var averagePlaysPerDay = (decimal)actualPlays / daysActive;

        return new AdvertisementAnalyticsDto
        {
            AdvertisementId = advertisement.Id,
            Name = advertisement.Name,
            TargetPlays = advertisement.TargetPlays,
            ActualPlays = actualPlays,
            PlayPercentage = playPercentage,
            ApprovalAmount = advertisement.ApprovalAmount,
            PaidAmount = advertisement.PaidAmount,
            Status = advertisement.Status.ToString(),
            StartDate = advertisement.StartDate,
            EndDate = advertisement.EndDate,
            DaysActive = daysActive,
            AveragePlaysPerDay = averagePlaysPerDay
        };
    }

    public async Task<FileUploadResponse> UploadVideoAsync(Guid advertisementId, Guid clientId, Stream fileStream, string fileName)
    {
        var advertisement = await _advertisementRepository.GetByIdAsync(advertisementId);
        if (advertisement == null)
            throw new NotFoundException($"Advertisement with ID {advertisementId} not found");

        if (advertisement.ClientId != clientId)
            throw new AuthorizationException("You can only upload videos for your own advertisements");

        if (fileStream.Length > MaxVideoFileSize)
            throw new Application.Exceptions.ValidationException($"Video file size must not exceed {MaxVideoFileSize / (1024 * 1024)}MB");

        var allowedExtensions = new[] { ".mp4", ".webm", ".mov" };
        var fileExtension = Path.GetExtension(fileName).ToLower();
        if (!allowedExtensions.Contains(fileExtension))
            throw new Application.Exceptions.ValidationException($"Video file type not allowed. Allowed types: {string.Join(", ", allowedExtensions)}");

        var blobFileName = $"ads/{advertisementId}/video_{DateTime.UtcNow:yyyyMMddHHmmss}{fileExtension}";

        var fileUrl = await _fileStorageService.UploadFileAsync("advertisements", blobFileName, fileStream);
        advertisement.VideoUrl = fileUrl;
        advertisement.UpdatedAt = DateTime.UtcNow;
        await _advertisementRepository.UpdateAsync(advertisement);

        _logger.LogInformation("Video uploaded for advertisement: {AdvertisementId}", advertisementId);

        return new FileUploadResponse
        {
            Url = fileUrl,
            FileSize = fileStream.Length,
            UploadedAt = DateTime.UtcNow
        };
    }

    public async Task<FileUploadResponse> UploadImageAsync(Guid advertisementId, Guid clientId, Stream fileStream, string fileName)
    {
        var advertisement = await _advertisementRepository.GetByIdAsync(advertisementId);
        if (advertisement == null)
            throw new NotFoundException($"Advertisement with ID {advertisementId} not found");

        if (advertisement.ClientId != clientId)
            throw new AuthorizationException("You can only upload images for your own advertisements");

        if (fileStream.Length > MaxImageFileSize)
            throw new Application.Exceptions.ValidationException($"Image file size must not exceed {MaxImageFileSize / (1024 * 1024)}MB");

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var fileExtension = Path.GetExtension(fileName).ToLower();
        if (!allowedExtensions.Contains(fileExtension))
            throw new Application.Exceptions.ValidationException($"Image file type not allowed. Allowed types: {string.Join(", ", allowedExtensions)}");

        var blobFileName = $"ads/{advertisementId}/image_{DateTime.UtcNow:yyyyMMddHHmmss}{fileExtension}";

        var fileUrl = await _fileStorageService.UploadFileAsync("advertisements", blobFileName, fileStream);
        advertisement.ImageUrl = fileUrl;
        advertisement.UpdatedAt = DateTime.UtcNow;
        await _advertisementRepository.UpdateAsync(advertisement);

        _logger.LogInformation("Image uploaded for advertisement: {AdvertisementId}", advertisementId);

        return new FileUploadResponse
        {
            Url = fileUrl,
            FileSize = fileStream.Length,
            UploadedAt = DateTime.UtcNow
        };
    }

    private static AdvertisementDto MapToAdvertisementDto(Advertisement ad)
    {
        return new AdvertisementDto
        {
            Id = ad.Id,
            ClientId = ad.ClientId,
            Name = ad.Name,
            Brand = ad.Brand,
            Description = ad.Description,
            TargetArea = ad.TargetArea,
            VideoUrl = ad.VideoUrl,
            ImageUrl = ad.ImageUrl,
            VideoDurationSeconds = ad.VideoDurationSeconds,
            StartDate = ad.StartDate,
            EndDate = ad.EndDate,
            TargetPlays = ad.TargetPlays,
            ActualPlays = ad.ActualPlays,
            AssignedCabs = ad.AssignedCabs,
            Status = ad.Status.ToString(),
            ApprovalAmount = ad.ApprovalAmount,
            PaidAmount = ad.PaidAmount,
            RejectionReason = ad.RejectionReason,
            ApprovedBy = ad.ApprovedBy,
            ApprovedAt = ad.ApprovedAt,
            RejectedAt = ad.RejectedAt,
            SubmittedAt = ad.SubmittedAt,
            UpdatedAt = ad.UpdatedAt
        };
    }
}
