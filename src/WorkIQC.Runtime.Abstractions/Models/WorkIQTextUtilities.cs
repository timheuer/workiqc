namespace WorkIQC.Runtime.Abstractions.Models;

public static class WorkIQTextUtilities
{
    public static string BuildConversationTitle(string prompt, int maxLength = 42)
    {
        var singleLine = prompt.Replace(Environment.NewLine, " ").Trim();
        return singleLine.Length <= maxLength ? singleLine : string.Concat(singleLine[..maxLength].TrimEnd(), "…");
    }

    public static string SummarizePrompt(string prompt, int maxLength = 120)
    {
        var normalized = prompt.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maxLength ? $"\"{normalized}\"" : $"\"{normalized[..maxLength].TrimEnd()}…\"";
    }

    public static string? NormalizeModelId(string? modelId)
        => string.IsNullOrWhiteSpace(modelId) ? null : modelId.Trim();
}
