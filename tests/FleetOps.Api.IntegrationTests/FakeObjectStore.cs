using System.Collections.Concurrent;
using FleetOps.Kernel.Files;

namespace FleetOps.Tests;

public sealed class FakeObjectStore : IObjectStore
{
    private readonly ConcurrentDictionary<string, ObjectInfo> objects = new(StringComparer.Ordinal);

    public ConcurrentQueue<(string Key, long SizeBytes, string ContentType)> Uploaded { get; } = new();
    public ConcurrentQueue<string> Deleted { get; } = new();
    public ConcurrentQueue<string> Linked { get; } = new();
    public bool ThrowStorageUnavailable { get; set; }

    public Task<ObjectLink> CreateUploadLinkAsync(string key, string contentType, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        if (ThrowStorageUnavailable)
        {
            throw new FleetOps.Api.Files.StorageUnavailableException();
        }

        Linked.Enqueue(key);
        return Task.FromResult(new ObjectLink($"https://objects.test/{key}?signature={Guid.NewGuid():N}", DateTimeOffset.UtcNow.Add(lifetime)));
    }

    public Task<ObjectLink> CreateDownloadLinkAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken) =>
        Task.FromResult(new ObjectLink($"https://objects.test/{key}", DateTimeOffset.UtcNow.Add(lifetime)));

    public Task<ObjectInfo?> GetInfoAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(objects.TryGetValue(key, out var info) ? info : null);

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        objects.TryRemove(key, out _);
        Deleted.Enqueue(key);
        return Task.CompletedTask;
    }

    public void Put(string key, long sizeBytes, string contentType)
    {
        objects[key] = new ObjectInfo(sizeBytes, contentType);
        Uploaded.Enqueue((key, sizeBytes, contentType));
    }
}
