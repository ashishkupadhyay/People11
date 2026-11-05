using People.Core.Models;

namespace People.Core.Interfaces;

public class ConnectionStateChangedEventArgs : EventArgs
{
    public string PlatformId { get; }
    public ConnectionState State { get; }
    public string? ErrorMessage { get; }
    
    public ConnectionStateChangedEventArgs(string platformId, ConnectionState state, string? errorMessage = null)
    {
        PlatformId = platformId;
        State = state;
        ErrorMessage = errorMessage;
    }
}

public class MessageReceivedEventArgs : EventArgs
{
    public Message Message { get; }
    
    public MessageReceivedEventArgs(Message message)
    {
        Message = message;
    }
}

public class ChatUpdatedEventArgs : EventArgs
{
    public Chat? Chat { get; }
    
    public ChatUpdatedEventArgs(Chat? chat = null)
    {
        Chat = chat;
    }
}
