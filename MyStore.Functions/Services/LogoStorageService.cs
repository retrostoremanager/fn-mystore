using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace MyStore.Functions.Services;

/// <summary>
/// Stores and retrieves company logos in Azure Blob Storage (EPIC-0-007-004).
/// Uses AzureWebJobsStorage connection string. Container: company-logos.
/// </summary>
public class LogoStorageService
{
    private const string ContainerName = "company-logos";
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/svg+xml"
    };
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".svg"
    };

    private readonly BlobContainerClient _containerClient;

    public LogoStorageService()
    {
        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? throw new InvalidOperationException("AzureWebJobsStorage is not configured.");
        var blobServiceClient = new BlobServiceClient(connectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
    }

    /// <summary>
    /// Uploads logo bytes to blob storage and returns the blob URL.
    /// </summary>
    /// <param name="companyId">Company ID (used for blob path)</param>
    /// <param name="fileBytes">File content</param>
    /// <param name="fileName">Original file name (for extension)</param>
    /// <param name="contentType">MIME type</param>
    /// <returns>Blob URL on success</returns>
    public async Task<string> UploadAsync(int companyId, byte[] fileBytes, string fileName, string contentType)
    {
        if (fileBytes.Length > MaxFileSizeBytes)
            throw new ArgumentException($"File exceeds 5MB limit. Size: {fileBytes.Length} bytes.");

        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            throw new ArgumentException($"Invalid file format. Allowed: PNG, JPG, SVG. Got: {fileName}");

        if (!AllowedContentTypes.Contains(contentType))
            throw new ArgumentException($"Invalid content type. Allowed: image/png, image/jpeg, image/svg+xml. Got: {contentType}");

        await _containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

        var blobName = $"{companyId}/logo{ext}";
        var blobClient = _containerClient.GetBlobClient(blobName);

        using var stream = new MemoryStream(fileBytes);
        await blobClient.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Conditions = null
        });

        return blobClient.Uri.ToString();
    }

    /// <summary>
    /// Deletes the company logo blob if it exists.
    /// </summary>
    public async Task DeleteAsync(int companyId)
    {
        foreach (var ext in AllowedExtensions)
        {
            var blobName = $"{companyId}/logo{ext}";
            var blobClient = _containerClient.GetBlobClient(blobName);
            await blobClient.DeleteIfExistsAsync();
        }
    }
}
