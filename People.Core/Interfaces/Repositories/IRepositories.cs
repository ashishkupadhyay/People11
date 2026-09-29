using People.Core.Models;

namespace People.Core.Interfaces.Repositories;

public interface IChatRepository
{
    Task<Chat?> GetByIdAsync(string id);
    Task<IReadOnlyList<Chat>> GetAllAsync(int offset = 0, int limit = 50);
    Task<IReadOnlyList<Chat>> GetByPlatformAsync(string platformId, int offset = 0, int limit = 50);
    Task SaveAsync(Chat chat);
    Task DeleteAsync(string id);
}

public interface IMessageRepository
{
    Task<Message?> GetByIdAsync(string id);
    Task<IReadOnlyList<Message>> GetByChatIdAsync(string chatId, int offset = 0, int limit = 50);
    Task SaveAsync(Message message);
    Task SaveManyAsync(IEnumerable<Message> messages);
    Task DeleteAsync(string id);
}

public interface IContactRepository
{
    Task<Contact?> GetByIdAsync(string id);
    Task<IReadOnlyList<Contact>> GetAllAsync();
    Task SaveAsync(Contact contact);
    Task DeleteAsync(string id);
}
