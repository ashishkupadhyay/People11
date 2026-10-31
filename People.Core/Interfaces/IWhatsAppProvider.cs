using System;
using System.Threading;
using System.Threading.Tasks;
using People.Core.Models;

namespace People.Core.Interfaces;

public interface IWhatsAppProvider : IMessagingProvider
{
    bool IsAuthenticated { get; }
    event EventHandler<string>? QrCodeReceived;
    event EventHandler<int>? SyncProgressChanged;
    Task AuthenticateAsync(CancellationToken ct = default);
    Task<System.Collections.Generic.IReadOnlyList<Contact>> GetContactsAsync(CancellationToken ct = default);
}
