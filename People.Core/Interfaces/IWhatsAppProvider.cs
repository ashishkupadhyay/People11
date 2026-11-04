using System;
using System.Threading;
using System.Threading.Tasks;
using People.Core.Models;

namespace People.Core.Interfaces;

public interface IWhatsAppProvider : IMessagingProvider
{
    bool IsAuthenticated { get; }
    bool IsClientReady { get; }
    event EventHandler<string>? QrCodeReceived;
    event EventHandler<int>? SyncProgressChanged;
    event EventHandler<string>? ChatMarkedRead;
    Task AuthenticateAsync(CancellationToken ct = default);
    Task ReauthenticateAsync(CancellationToken ct = default);
    Task<System.Collections.Generic.IReadOnlyList<Contact>> GetContactsAsync(CancellationToken ct = default);
    System.Collections.Generic.IReadOnlyList<Chat> GetCachedChats();
    System.Collections.Generic.IReadOnlyList<Message> GetCachedMessages(string chatId, int limit = 30);
    string? GetContactName(string chatId);
    string? GetPushName(string chatId);
    string? GetPhoneNumber(string chatId);
    Task<string?> GetProfilePictureUrlAsync(string chatId, CancellationToken ct = default);
    Task MarkChatAsReadAsync(string chatId, CancellationToken ct = default);
    void SubscribePresence(string chatId);
}
