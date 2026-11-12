using FleetOps.Kernel.Authorization;

namespace FleetOps.Kernel.Files;

[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record ObjectLink(string Url, DateTimeOffset ExpiresAt) { public override string ToString() => "[object-link]"; }
public sealed record ObjectInfo(long SizeBytes, string ContentType);
public interface IObjectStore
{
    Task WriteAsync(string key, ReadOnlyMemory<byte> contents, string contentType, CancellationToken cancellationToken) => throw new NotSupportedException("object_write_not_supported");
    Task<ObjectLink> CreateUploadLinkAsync(string key, string contentType, TimeSpan lifetime, CancellationToken cancellationToken);
    Task<ObjectLink> CreateDownloadLinkAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken);
    Task<ObjectInfo?> GetInfoAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
