namespace WorkIQC.Persistence.Models;

public class Session
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ConversationId { get; set; } = string.Empty;
    public string? CopilotSessionId { get; set; } // SDK session ID for resume
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Conversation? Conversation { get; set; }
}
