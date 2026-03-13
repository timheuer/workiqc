using Microsoft.EntityFrameworkCore;
using WorkIQC.Persistence.Models;

namespace WorkIQC.Persistence;

public class WorkIQDbContext : DbContext
{
    public DbSet<Conversation> Conversations { get; set; } = null!;
    public DbSet<Message> Messages { get; set; } = null!;
    public DbSet<Session> Sessions { get; set; } = null!;

    public WorkIQDbContext(DbContextOptions<WorkIQDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Conversation configuration
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();
            entity.HasIndex(e => e.UpdatedAt); // For recent-first ordering
        });

        // Message configuration
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Timestamp).IsRequired();
            entity.HasIndex(e => new { e.ConversationId, e.Timestamp }); // For ordering within conversation

            entity.HasOne(e => e.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Session configuration
        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ConversationId).IsRequired();
            entity.HasIndex(e => e.ConversationId).IsUnique(); // One session per conversation

            entity.HasOne(e => e.Conversation)
                .WithOne(c => c.Session)
                .HasForeignKey<Session>(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
