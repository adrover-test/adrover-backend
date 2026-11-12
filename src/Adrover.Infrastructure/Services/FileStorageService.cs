using Azure.Storage.Blobs;
using Azure.Storage.Sas;

namespace Adrover.Infrastructure.Services;

/// <summary>
/// Azure Blob Storage service for file management
/// </summary>
public interface IFileStorageService
{
    Task<string> UploadFileAsync(string containerName, string fileName, Stream fileStream);
    Task<bool> DeleteFileAsync(string containerName, string fileName);
    Task<Stream> DownloadFileAsync(string containerName, string fileName);
    Task<string> GenerateSasUrlAsync(string containerName, string fileName, TimeSpan expiration);
    Task<IEnumerable<string>> ListFilesAsync(string containerName);
}

public class AzureBlobStorageService : IFileStorageService
{
    private readonly BlobServiceClient? _blobServiceClient;

    public AzureBlobStorageService(string connectionString)
    {
        try
        {
            _blobServiceClient = new BlobServiceClient(connectionString);
        }
        catch
        {
            // During migrations or local development, connection string might not be valid
            // The service will fail at runtime if actually used without proper configuration
            _blobServiceClient = null;
        }
    }

    public async Task<string> UploadFileAsync(string containerName, string fileName, Stream fileStream)
    {
        if (_blobServiceClient == null)
            throw new InvalidOperationException("Azure Blob Storage is not configured. Check your connection string in appsettings.json");

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync();

        var blobClient = containerClient.GetBlobClient(fileName);
        await blobClient.UploadAsync(fileStream, overwrite: true);

        return blobClient.Uri.ToString();
    }

    public async Task<bool> DeleteFileAsync(string containerName, string fileName)
    {
        if (_blobServiceClient == null)
            return false;

        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(fileName);
            await blobClient.DeleteAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<Stream> DownloadFileAsync(string containerName, string fileName)
    {
        if (_blobServiceClient == null)
            throw new InvalidOperationException("Azure Blob Storage is not configured. Check your connection string in appsettings.json");

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(fileName);
        var download = await blobClient.DownloadAsync();
        return download.Value.Content;
    }

    public async Task<string> GenerateSasUrlAsync(string containerName, string fileName, TimeSpan expiration)
    {
        if (_blobServiceClient == null)
            throw new InvalidOperationException("Azure Blob Storage is not configured. Check your connection string in appsettings.json");

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(fileName);

        var sasUri = blobClient.GenerateSasUri(BlobSasPermissions.Read, DateTime.UtcNow.Add(expiration));
        return sasUri.ToString();
    }

    public async Task<IEnumerable<string>> ListFilesAsync(string containerName)
    {
        if (_blobServiceClient == null)
            return Enumerable.Empty<string>();

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobs = new List<string>();

        await foreach (var blobItem in containerClient.GetBlobsAsync())
        {
            blobs.Add(blobItem.Name);
        }

        return blobs;
    }
}
