using System;
using System.Threading;
using System.Threading.Tasks;
using People.Core.Models;

namespace People.Core.Interfaces;

public interface IWhatsAppProvider : IMessagingProvider
{
    event EventHandler<string>? QrCodeReceived;
    Task AuthenticateAsync(CancellationToken ct = default);
}
