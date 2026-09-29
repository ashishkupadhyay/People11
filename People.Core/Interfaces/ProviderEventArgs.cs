using People.Core.Models;

namespace People.Core.Interfaces;

public class ConnectionStateChangedEventArgs : EventArgs
{
    public string PlatformId { get; }
    public ConnectionState State { get; }
    
    public ConnectionStateChangedEventArgs(string platformId, ConnectionState state)
    {
        PlatformId = platformId;
        State = state;
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
    public Chat Chat { get; }
    
    public ChatUpdatedEventArgs(Chat chat)
    {
        Chat = chat;
    }
}
