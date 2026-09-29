using People.Core.Models;

namespace People.Core.Interfaces;

public interface IMessagingProvider : IDisposable
{
    string PlatformId { get; }
    string DisplayName { get; }
    ConnectionState ConnectionState { get; }
    
    event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
    event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    event EventHandler<ChatUpdatedEventArgs>? ChatUpdated;

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    
    Task<IReadOnlyList<Chat>> GetChatsAsync(int offset = 0, int limit = 50, CancellationToken ct = default);
    Task<IReadOnlyList<Message>> GetMessagesAsync(string chatId, int offset = 0, int limit = 50, CancellationToken ct = default);
    
    Task<Message> SendMessageAsync(string chatId, MessageContent content, CancellationToken ct = default);
    Task MarkAsReadAsync(string chatId, string messageId, CancellationToken ct = default);
}
