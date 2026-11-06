using Amazon.S3;
using Amazon.S3.Model;
using FleetOps.Kernel.Files;

namespace FleetOps.Api.Files;

public sealed class StorageUnavailableException : Exception { public StorageUnavailableException() : base("storage_unavailable") { } }
public sealed class StorageOptions
{
    public string? Endpoint { get; set; }
    public string? Region { get; set; }
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public string? Bucket { get; set; }
    public int LinkMinutes { get; set; } = 5;
    public long MaxFileBytes { get; set; } = 15728640;
}
public sealed class S3ObjectStore(IConfiguration configuration) : IObjectStore
{
    private readonly StorageOptions options = configuration.GetSection("Storage").Get<StorageOptions>() ?? new();
    private AmazonS3Client Client => string.IsNullOrWhiteSpace(options.Endpoint) || string.IsNullOrWhiteSpace(options.Region) || string.IsNullOrWhiteSpace(options.AccessKeyId) || string.IsNullOrWhiteSpace(options.SecretAccessKey) || string.IsNullOrWhiteSpace(options.Bucket) ? throw new StorageUnavailableException() : new(new Amazon.Runtime.BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey), new AmazonS3Config { ServiceURL = options.Endpoint, AuthenticationRegion = options.Region, ForcePathStyle = true });
    public async Task<ObjectLink> CreateUploadLinkAsync(string key, string contentType, TimeSpan lifetime, CancellationToken cancellationToken) { using var client = Client; var expires = DateTime.UtcNow.Add(lifetime); var url = await client.GetPreSignedURLAsync(new GetPreSignedUrlRequest { BucketName = options.Bucket, Key = key, Verb = HttpVerb.PUT, ContentType = contentType, Expires = expires }); return new(url, new DateTimeOffset(expires, TimeSpan.Zero)); }
    public async Task<ObjectLink> CreateDownloadLinkAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken) { using var client = Client; var expires = DateTime.UtcNow.Add(lifetime); var url = await client.GetPreSignedURLAsync(new GetPreSignedUrlRequest { BucketName = options.Bucket, Key = key, Verb = HttpVerb.GET, Expires = expires }); return new(url, new DateTimeOffset(expires, TimeSpan.Zero)); }
    public async Task<ObjectInfo?> GetInfoAsync(string key, CancellationToken cancellationToken) { using var client = Client; try { var response = await client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = options.Bucket, Key = key }, cancellationToken); return new(response.ContentLength, response.Headers.ContentType); } catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; } }
    public async Task DeleteAsync(string key, CancellationToken cancellationToken) { using var client = Client; await client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = options.Bucket, Key = key }, cancellationToken); }
}
