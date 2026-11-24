namespace Adrover.Functions.Contracts;

public sealed record ContentProcessingMessage(
    string MessageType,
    Guid AdvertisementId,
    int AssetVersion,
    string VideoBlobPath,
    Guid? PaymentId,
    DateTime RequestedAtUtc,
    int PollAttempt = 0);
