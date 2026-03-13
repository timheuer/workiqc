using Microsoft.EntityFrameworkCore;
using WorkIQC.Persistence.Models;

namespace WorkIQC.Persistence.Services;

public class ConversationService : IConversationService
{
    private readonly WorkIQDbContext _context;

    public ConversationService(WorkIQDbContext context)
    {
        _context = context;
    }

    public async Task<Conversation> CreateConversationAsync(string? title = null)
    {
        var conversation = new Conversation
        {
            Title = title ?? "New Chat"
        };

        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync();

        return conversation;
    }

    public async Task<Conversation?> GetConversationAsync(string id)
    {
        return await _context.Conversations
            .Include(c => c.Messages.OrderBy(m => m.Timestamp))
            .Include(c => c.Session)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<IReadOnlyList<Conversation>> GetRecentConversationsAsync(int limit = 50)
    {
        return await _context.Conversations
            .OrderByDescending(c => c.UpdatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task UpdateConversationAsync(Conversation conversation)
    {
        conversation.UpdatedAt = DateTime.UtcNow;
        _context.Conversations.Update(conversation);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteConversationAsync(string id)
    {
        var conversation = await _context.Conversations.FindAsync(id);
        if (conversation != null)
        {
            _context.Conversations.Remove(conversation);
            await _context.SaveChangesAsync();
        }
    }

    public async Task AddMessageAsync(string conversationId, string role, string content, string? metadata = null)
    {
        var message = new Message
        {
            ConversationId = conversationId,
            Role = role,
            Content = content,
            Metadata = metadata
        };

        _context.Messages.Add(message);

        // Update conversation timestamp
        var conversation = await _context.Conversations.FindAsync(conversationId);
        if (conversation != null)
        {
            conversation.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<Message>> GetMessagesAsync(string conversationId)
    {
        return await _context.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();
    }

    public async Task SetCopilotSessionIdAsync(string conversationId, string copilotSessionId)
    {
        var session = await _context.Sessions.FirstOrDefaultAsync(s => s.ConversationId == conversationId);
        
        if (session != null)
        {
            session.CopilotSessionId = copilotSessionId;
        }
        else
        {
            session = new Session
            {
                ConversationId = conversationId,
                CopilotSessionId = copilotSessionId
            };
            _context.Sessions.Add(session);
        }

        await _context.SaveChangesAsync();
    }

    public async Task<string?> GetCopilotSessionIdAsync(string conversationId)
    {
        var session = await _context.Sessions.FirstOrDefaultAsync(s => s.ConversationId == conversationId);
        return session?.CopilotSessionId;
    }
}
