using WorkIQC.App.Services;

namespace WorkIQC.App.Tests;

[TestClass]
public sealed class MessageCopyFormatterTests
{
    [TestMethod]
    public void PrepareForClipboard_PreservesStructuredMarkdownSections()
    {
        const string content = """
            Here's what I found looking across all three of your calendars, with large-group and recurring syncs treated as overrideable:

            ---

            ## Clean Slots (no conflicts)

            | # | Day/Time | Notes |
            | --- | --- | --- |
            | 1 | Wed 8:00-8:25 AM (weekly) | Completely clear for all 3 |
            | 2 | Fri 9:00-9:25 AM (weekly) | No standing conflicts any week |
            """;

        var copied = MessageCopyFormatter.PrepareForClipboard(content);

        Assert.AreEqual(content, copied);
    }

    [TestMethod]
    public void PrepareForClipboard_StripsRecognizedFollowUpSuggestionSection()
    {
        const string content = """
            Here's the summary you asked for.

            ---

            **Suggested follow-ups**
            - Draft a follow-up email for this meeting
            - Turn this into a Teams post
            """;

        var copied = MessageCopyFormatter.PrepareForClipboard(content);

        Assert.AreEqual("Here's the summary you asked for.", copied);
    }

    [TestMethod]
    public void PrepareForClipboard_ReturnsEmptyStringForBlankContent()
    {
        Assert.AreEqual(string.Empty, MessageCopyFormatter.PrepareForClipboard("   "));
    }
}
