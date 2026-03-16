using System.Text.RegularExpressions;

namespace WorkIQC.App.Services;

internal static partial class MessageCopyFormatter
{
    private static readonly string[] FollowUpSectionLabels =
    [
        "suggested follow-ups",
        "suggested follow up",
        "follow-up suggestions",
        "follow up suggestions",
        "follow-up questions",
        "follow up questions",
        "you can also ask",
        "try asking",
        "suggested prompts"
    ];

    public static string PrepareForClipboard(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var trimmedContent = content.TrimEnd();
        var followUpBoundary = FindTrailingFollowUpBoundary(trimmedContent);
        return followUpBoundary >= 0
            ? trimmedContent[..followUpBoundary].TrimEnd()
            : trimmedContent;
    }

    private static int FindTrailingFollowUpBoundary(string content)
    {
        var lines = content.Split('\n');
        var lineStartIndices = GetLineStartIndices(lines);

        for (var lineIndex = lines.Length - 1; lineIndex >= 0; lineIndex--)
        {
            if (!IsHorizontalRule(lines[lineIndex]))
            {
                continue;
            }

            if (LooksLikeFollowUpBlock(lines[(lineIndex + 1)..]))
            {
                return lineStartIndices[lineIndex];
            }
        }

        return -1;
    }

    private static int[] GetLineStartIndices(string[] lines)
    {
        var lineStartIndices = new int[lines.Length];
        var currentIndex = 0;

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            lineStartIndices[lineIndex] = currentIndex;
            currentIndex += lines[lineIndex].Length + 1;
        }

        return lineStartIndices;
    }

    private static bool LooksLikeFollowUpBlock(string[] trailingLines)
    {
        var nonEmptyLines = trailingLines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        if (nonEmptyLines.Length < 2 || nonEmptyLines.Length > 8)
        {
            return false;
        }

        var normalizedHeading = NormalizeMarkdownLabel(nonEmptyLines[0]);
        if (!FollowUpSectionLabels.Any(label => normalizedHeading.Contains(label, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return nonEmptyLines[1..].All(LooksLikeSuggestionLine);
    }

    private static string NormalizeMarkdownLabel(string line)
    {
        var trimmed = line.Trim();
        trimmed = trimmed.TrimStart('#', '>', '-', '*', '+', ' ');
        trimmed = trimmed.Trim('*', '_', '`', ' ');
        return trimmed.TrimEnd(':');
    }

    private static bool IsHorizontalRule(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 3
            && (trimmed.Replace("-", "", StringComparison.Ordinal) == string.Empty
                || trimmed.Replace("*", "", StringComparison.Ordinal) == string.Empty
                || trimmed.Replace("_", "", StringComparison.Ordinal) == string.Empty);
    }

    private static bool LooksLikeSuggestionLine(string line)
    {
        if (line.StartsWith("- ", StringComparison.Ordinal)
            || line.StartsWith("* ", StringComparison.Ordinal)
            || line.StartsWith("+ ", StringComparison.Ordinal))
        {
            return true;
        }

        return NumberedSuggestionPattern().IsMatch(line) || line.EndsWith("?", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^\d+[\.\)]\s+")]
    private static partial Regex NumberedSuggestionPattern();
}
