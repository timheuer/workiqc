using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkIQC.Persistence.Models;
using WorkIQC.Persistence.Services;

namespace WorkIQC.Persistence.Tests;

[TestClass]
public sealed class ConversationServiceTests
{
    [TestMethod]
    public async Task CreateConversationAsync_UsesDefaultTitleAndPersistsConversation()
    {
        await using var fixture = await ConversationServiceFixture.CreateAsync();

        var created = await fixture.Service.CreateConversationAsync();
        var reloaded = await fixture.Service.GetConversationAsync(created.Id);

        Assert.AreEqual("New Chat", created.Title);
        Assert.IsNotNull(reloaded);
        Assert.AreEqual(created.Id, reloaded!.Id);
        Assert.IsEmpty(reloaded.Messages);
        Assert.IsNull(reloaded.Session);
    }

    [TestMethod]
    public async Task AddMessageAsync_PersistsUnicodeMetadataAndRefreshesConversationTimestamp()
    {
        await using var fixture = await ConversationServiceFixture.CreateAsync();
        var conversation = await fixture.Service.CreateConversationAsync("Unicode");
        var originalUpdatedAt = conversation.UpdatedAt;

        await Task.Delay(25);
        await fixture.Service.AddMessageAsync(conversation.Id, "user", "Hello 👋");
        await Task.Delay(25);
        await fixture.Service.AddMessageAsync(
            conversation.Id,
            "assistant",
            "你好 — مرحبا",
            """{"source":"tool","emoji":"🧪"}""");

        var reloaded = await fixture.Service.GetConversationAsync(conversation.Id);

        Assert.IsNotNull(reloaded);
        Assert.IsTrue(reloaded!.UpdatedAt > originalUpdatedAt);
        var messages = reloaded.Messages.OrderBy(message => message.Timestamp).ToArray();
        Assert.HasCount(2, messages);
        Assert.AreEqual("user", messages[0].Role);
        Assert.AreEqual("Hello 👋", messages[0].Content);
        Assert.AreEqual("assistant", messages[1].Role);
        Assert.AreEqual("你好 — مرحبا", messages[1].Content);
        Assert.AreEqual("""{"source":"tool","emoji":"🧪"}""", messages[1].Metadata);
    }

    [TestMethod]
    public async Task AddMessageAsync_PreservesMarkdownBlocksAsPlainText()
    {
        await using var fixture = await ConversationServiceFixture.CreateAsync();
        var conversation = await fixture.Service.CreateConversationAsync("Markdown");
        const string markdown = """
            ## Release checklist
            
            - keep **bold** markers literal
            - preserve `inline code`
            
            ```csharp
            Console.WriteLine("ship it");
            ```
            
            | Name | Value |
            | --- | --- |
            | Theme | Dark |
            """;

        await fixture.Service.AddMessageAsync(conversation.Id, "assistant", markdown);

        var reloaded = await fixture.Service.GetConversationAsync(conversation.Id);

        Assert.IsNotNull(reloaded);
        var persisted = reloaded!.Messages.Single();
        Assert.AreEqual(markdown.ReplaceLineEndings(Environment.NewLine), persisted.Content.ReplaceLineEndings(Environment.NewLine));
    }

    [TestMethod]
    public async Task AddMessageAsync_RejectsUnknownConversationId()
    {
        await using var fixture = await ConversationServiceFixture.CreateAsync();

        var exception = await ThrowsAsync<DbUpdateException>(() =>
            fixture.Service.AddMessageAsync(Guid.NewGuid().ToString(), "user", "orphaned"));

        Assert.IsNotNull(exception.InnerException);
    }

    [TestMethod]
    public async Task SetCopilotSessionIdAsync_UpdatesExistingSessionWithoutCreatingDuplicates()
    {
        await using var fixture = await ConversationServiceFixture.CreateAsync();
        var conversation = await fixture.Service.CreateConversationAsync("Resume me");

        await fixture.Service.SetCopilotSessionIdAsync(conversation.Id, "session-1");
        await fixture.Service.SetCopilotSessionIdAsync(conversation.Id, "session-2");

        var persistedSessionId = await fixture.Service.GetCopilotSessionIdAsync(conversation.Id);
        var sessionCount = await fixture.Context.Sessions.CountAsync(session => session.ConversationId == conversation.Id);

        Assert.AreEqual("session-2", persistedSessionId);
        Assert.AreEqual(1, sessionCount);
    }

    [TestMethod]
    public async Task DeleteConversationAsync_CascadesMessagesAndSessionRows()
    {
        await using var fixture = await ConversationServiceFixture.CreateAsync();
        var conversation = await fixture.Service.CreateConversationAsync("Disposable");
        await fixture.Service.AddMessageAsync(conversation.Id, "user", "keep me?");
        await fixture.Service.SetCopilotSessionIdAsync(conversation.Id, "session-delete");

        await fixture.Service.DeleteConversationAsync(conversation.Id);

        Assert.IsNull(await fixture.Service.GetConversationAsync(conversation.Id));
        Assert.AreEqual(0, await fixture.Context.Messages.CountAsync(message => message.ConversationId == conversation.Id));
        Assert.AreEqual(0, await fixture.Context.Sessions.CountAsync(session => session.ConversationId == conversation.Id));
    }

    [TestMethod]
    public async Task GetRecentConversationsAsync_ReturnsRecentFirstAndHonorsLimit()
    {
        await using var fixture = await ConversationServiceFixture.CreateAsync();
        var oldest = await fixture.Service.CreateConversationAsync("Oldest");
        var middle = await fixture.Service.CreateConversationAsync("Middle");
        var newest = await fixture.Service.CreateConversationAsync("Newest");

        fixture.Context.Conversations.UpdateRange(
            RewriteTimestamp(oldest, DateTime.UtcNow.AddHours(-3)),
            RewriteTimestamp(middle, DateTime.UtcNow.AddHours(-2)),
            RewriteTimestamp(newest, DateTime.UtcNow.AddHours(-1)));
        await fixture.Context.SaveChangesAsync();

        var recent = await fixture.Service.GetRecentConversationsAsync(limit: 2);

        Assert.HasCount(2, recent);
        Assert.AreEqual(newest.Id, recent[0].Id);
        Assert.AreEqual(middle.Id, recent[1].Id);
    }

    private static async Task<TException> ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new AssertFailedException($"Expected exception of type {typeof(TException).Name}.");
    }

    private static Conversation RewriteTimestamp(Conversation conversation, DateTime updatedAt)
    {
        conversation.UpdatedAt = updatedAt;
        return conversation;
    }

    private sealed class ConversationServiceFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ConversationServiceFixture(SqliteConnection connection, WorkIQDbContext context, ConversationService service)
        {
            _connection = connection;
            Context = context;
            Service = service;
        }

        public WorkIQDbContext Context { get; }

        public ConversationService Service { get; }

        public static async Task<ConversationServiceFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<WorkIQDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new WorkIQDbContext(options);
            await context.Database.EnsureCreatedAsync();

            return new ConversationServiceFixture(connection, context, new ConversationService(context));
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
