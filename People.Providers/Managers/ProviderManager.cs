using People.Core.Interfaces;
using People.Core.Models;
using System.Collections.Concurrent;

namespace People.Providers;

public class ProviderManager
{
    private readonly IEnumerable<IMessagingProvider> _providers;
    private readonly ConcurrentDictionary<string, IMessagingProvider> _providerMap;

    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
    public event EventHandler<ChatUpdatedEventArgs>? ChatUpdated;

    public ProviderManager(IEnumerable<IMessagingProvider> providers)
    {
        _providers = providers;
        _providerMap = new ConcurrentDictionary<string, IMessagingProvider>();

        foreach (var provider in _providers)
        {
            _providerMap[provider.PlatformId] = provider;
            
            // Wire up events
            provider.MessageReceived += OnProviderMessageReceived;
            provider.ConnectionStateChanged += OnProviderConnectionStateChanged;
            provider.ChatUpdated += OnProviderChatUpdated;
        }
    }

    public IMessagingProvider? GetProvider(string platformId)
    {
        _providerMap.TryGetValue(platformId, out var provider);
        return provider;
    }

    public IEnumerable<IMessagingProvider> GetAllProviders()
    {
        return _providers;
    }

    public async Task ConnectAllAsync(CancellationToken ct = default)
    {
        var connectTasks = _providers.Select(p => p.ConnectAsync(ct));
        await Task.WhenAll(connectTasks);
    }

    public async Task DisconnectAllAsync()
    {
        var disconnectTasks = _providers.Select(p => p.DisconnectAsync());
        await Task.WhenAll(disconnectTasks);
    }

    private void OnProviderMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        MessageReceived?.Invoke(this, e);
    }

    private void OnProviderConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        ConnectionStateChanged?.Invoke(this, e);
    }

    private void OnProviderChatUpdated(object? sender, ChatUpdatedEventArgs e)
    {
        ChatUpdated?.Invoke(this, e);
    }
}
