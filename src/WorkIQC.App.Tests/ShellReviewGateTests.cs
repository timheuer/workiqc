using System.Text.RegularExpressions;

namespace WorkIQC.App.Tests;

[TestClass]
public sealed class ShellReviewGateTests
{
    [TestMethod]
    public void SettingsButton_HasNavigationWiring()
    {
        var xaml = ReadRepoFile("src", "WorkIQC.App", "Views", "MainPage.xaml");
        var settingsButton = Regex.Matches(
                xaml,
                """<Button\b(?<tag>[^>]*)>(?<body>.*?)</Button>""",
                RegexOptions.Singleline)
            .Cast<Match>()
            .FirstOrDefault(match => match.Groups["body"].Value.Contains("Settings", StringComparison.Ordinal));

        Assert.IsNotNull(settingsButton, "Expected to find the Settings button in MainPage.xaml.");
        Assert.IsTrue(
            Regex.IsMatch(settingsButton.Groups["tag"].Value, """\b(Click|Command)\s*=""", RegexOptions.Singleline),
            "The Settings button needs a click or command binding so the shell can reliably return to a settings surface.");
    }

    [TestMethod]
    public void TitleBarDragRegion_DoesNotRenderStatusPills()
    {
        var xaml = ReadRepoFile("src", "WorkIQC.App", "Views", "MainPage.xaml");
        var titleBarRegion = Regex.Match(
            xaml,
            """<Grid x:Name="TitleBarDragRegion"(?<body>.*?)</Grid>""",
            RegexOptions.Singleline);

        Assert.IsTrue(titleBarRegion.Success, "Expected to find the title bar drag region in MainPage.xaml.");
        Assert.IsFalse(
            titleBarRegion.Groups["body"].Value.Contains("StatusPill", StringComparison.Ordinal)
                || titleBarRegion.Groups["body"].Value.Contains("ConnectionBadgeText", StringComparison.Ordinal)
                || titleBarRegion.Groups["body"].Value.Contains("ActivityBadgeText", StringComparison.Ordinal),
            "The title bar should stay chrome-light; runtime status belongs in the shell content, not title-bar pills.");
    }

    [TestMethod]
    public void SidebarSettingsSurface_DoesNotRenderFooterParagraph()
    {
        var xaml = ReadRepoFile("src", "WorkIQC.App", "Views", "MainPage.xaml");

        Assert.IsFalse(
            xaml.Contains("SidebarFooterText", StringComparison.Ordinal),
            "The sidebar settings affordance should stay compact instead of rendering a paragraph under the Settings link.");
        StringAssert.Contains(
            xaml,
            "ConnectionBadgeText",
            "Runtime status should still appear somewhere in the main shell after leaving the title bar.");
    }

    [TestMethod]
    public void ShellService_ExposesConversationDeletion()
    {
        var contract = ReadRepoFile("src", "WorkIQC.App", "Services", "IChatShellService.cs");
        var viewModel = ReadRepoFile("src", "WorkIQC.App", "ViewModels", "MainPageViewModel.cs");

        StringAssert.Contains(
            contract,
            "DeleteConversationAsync",
            "The shell contract should expose conversation deletion so the UI can remove saved threads without reaching into persistence directly.");
        StringAssert.Contains(
            viewModel,
            "DeleteConversationAsync",
            "The main view-model should delegate thread deletion through the shell service.");
    }

    [TestMethod]
    public void RecentThreadsSurface_ExposesDeleteAffordance()
    {
        var xaml = ReadRepoFile("src", "WorkIQC.App", "Views", "MainPage.xaml");

        Assert.IsTrue(
            Regex.IsMatch(xaml, """(Delete|ContextFlyout|MenuFlyoutItem|SwipeControl)""", RegexOptions.IgnoreCase),
            "Recent threads need a visible delete affordance so users can remove saved conversations without breaking transcript/history behavior.");
    }

    [TestMethod]
    public void Composer_UsesPreviewKeyDownForEnterToSend()
    {
        var xaml = ReadRepoFile("src", "WorkIQC.App", "Views", "MainPage.xaml");
        var codeBehind = ReadRepoFile("src", "WorkIQC.App", "Views", "MainPage.xaml.cs");

        StringAssert.Contains(
            xaml,
            "AcceptsReturn=\"True\"",
            "The composer should stay multiline so Shift+Enter can insert a newline.");
        StringAssert.Contains(
            xaml,
            "PreviewKeyDown=\"OnComposerPreviewKeyDown\"",
            "The composer must intercept Enter before the WinUI TextBox consumes it for newline insertion.");
        Assert.IsFalse(
            xaml.Contains("KeyDown=\"OnComposerKeyDown\"", StringComparison.Ordinal),
            "The multiline composer should not rely on the regular KeyDown event for Enter-to-send.");
        StringAssert.Contains(
            codeBehind,
            "OnComposerPreviewKeyDown",
            "The preview key handler should live in the page code-behind so send behavior stays wired.");
    }

    private static string ReadRepoFile(params string[] relativeParts)
        => File.ReadAllText(Path.Combine([FindRepoRoot(), .. relativeParts]));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "WorkIQC.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
