using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
namespace AspNetCore.SignalR.Npgsql.Internal;

internal class SubscriptionManager
{
    private readonly ConcurrentDictionary<string, HubConnectionStore> _subscriptions = new ConcurrentDictionary<string, HubConnectionStore>(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

    public HubConnectionStore? Get(string? id)
    {
        return id is null ? null : _subscriptions.GetValueOrDefault(id);
    }

    public async Task AddSubscriptionAsync(string id, HubConnectionContext connection)
    {
        await _lock.WaitAsync();

        try
        {
            var subscription = _subscriptions.GetOrAdd(id, _ => new HubConnectionStore());

            subscription.Add(connection);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveSubscriptionAsync(string id, HubConnectionContext connection)
    {
        await _lock.WaitAsync();

        try
        {
            if (!_subscriptions.TryGetValue(id, out var subscription))
            {
                return;
            }

            subscription.Remove(connection);
        }
        finally
        {
            _lock.Release();
        }
    }
}
