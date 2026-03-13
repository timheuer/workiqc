using WorkIQC.Persistence.Models;

namespace WorkIQC.Persistence.Services;

public interface IConversationService
{
    Task<Conversation> CreateConversationAsync(string? title = null);
    Task<Conversation?> GetConversationAsync(string id);
    Task<IReadOnlyList<Conversation>> GetRecentConversationsAsync(int limit = 50);
    Task UpdateConversationAsync(Conversation conversation);
    Task DeleteConversationAsync(string id);
    
    Task AddMessageAsync(string conversationId, string role, string content, string? metadata = null);
    Task<IReadOnlyList<Message>> GetMessagesAsync(string conversationId);
    
    Task SetCopilotSessionIdAsync(string conversationId, string copilotSessionId);
    Task<string?> GetCopilotSessionIdAsync(string conversationId);
}
