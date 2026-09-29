namespace People.Core.Models;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    AuthenticationRequired,
    Error
}
