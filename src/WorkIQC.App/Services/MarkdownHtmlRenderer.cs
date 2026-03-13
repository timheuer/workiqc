using System.Text.RegularExpressions;
using Markdig;

namespace WorkIQC.App.Services;

internal static class MarkdownHtmlRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private static readonly Regex UnsafeAttributePattern = new(
        "\\s(?:href|src)\\s*=\\s*([\"'])\\s*(?:javascript:|data:)[^\"']*\\1",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string RenderDocument(string markdown, MarkdownPalette palette)
    {
        var renderedContent = string.IsNullOrWhiteSpace(markdown)
            ? "<p class=\"empty\"></p>"
            : UnsafeAttributePattern.Replace(Markdig.Markdown.ToHtml(markdown, Pipeline), string.Empty);

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta http-equiv="X-UA-Compatible" content="IE=edge">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <style>
        :root {
            color-scheme: light dark;
            --page-bg: {{palette.BackgroundColor}};
            --page-fg: {{palette.ForegroundColor}};
            --muted-fg: {{palette.MutedColor}};
            --accent: {{palette.AccentColor}};
            --surface-soft: {{palette.SecondarySurfaceColor}};
            --surface-border: {{palette.BorderColor}};
        }

        html, body {
            margin: 0;
            padding: 0;
            background: var(--page-bg);
            color: var(--page-fg);
            font-family: "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif;
            font-size: 14px;
            line-height: 1.65;
            overflow: hidden;
            word-break: break-word;
            width: 100%;
            max-width: 100%;
            box-sizing: border-box;
        }

        body {
            min-height: 1px;
        }

        #content > :first-child {
            margin-top: 0;
        }

        #content > :last-child {
            margin-bottom: 0;
        }

        p, ul, ol, blockquote, table, pre, hr {
            margin: 0 0 14px;
        }

        h1, h2, h3, h4 {
            margin: 0 0 12px;
            color: var(--page-fg);
            line-height: 1.28;
            font-weight: 650;
        }

        h1 { font-size: 1.55rem; }
        h2 { font-size: 1.35rem; }
        h3 { font-size: 1.15rem; }
        h4 { font-size: 1rem; }

        ul, ol {
            padding-left: 24px;
        }

        li + li {
            margin-top: 4px;
        }

        a {
            color: var(--accent);
            text-decoration-thickness: 1.5px;
            text-underline-offset: 2px;
        }

        strong {
            font-weight: 650;
        }

        code {
            font-family: "Cascadia Code", "Consolas", monospace;
            font-size: 0.93em;
            background: var(--surface-soft);
            border: 1px solid var(--surface-border);
            border-radius: 8px;
            padding: 0.12rem 0.38rem;
        }

        pre {
            background: var(--surface-soft);
            border: 1px solid var(--surface-border);
            border-radius: 14px;
            padding: 12px 14px;
            overflow-x: auto;
        }

        pre code {
            display: block;
            border: 0;
            background: transparent;
            padding: 0;
            white-space: pre;
        }

        blockquote {
            border-left: 3px solid var(--accent);
            padding: 2px 0 2px 14px;
            color: var(--muted-fg);
        }

        table {
            width: 100%;
            border-collapse: collapse;
            border-spacing: 0;
            border-radius: 14px;
            overflow: hidden;
        }

        th, td {
            border: 1px solid var(--surface-border);
            padding: 8px 10px;
            text-align: left;
            vertical-align: top;
        }

        th {
            background: var(--surface-soft);
            font-weight: 600;
        }

        hr {
            border: 0;
            border-top: 1px solid var(--surface-border);
        }

        .empty {
            min-height: 1px;
            margin: 0;
        }
    </style>
</head>
<body>
<div id="content">{{renderedContent}}</div>
</body>
</html>
""";
    }
}

internal readonly record struct MarkdownPalette(
    string BackgroundColor,
    string ForegroundColor,
    string MutedColor,
    string AccentColor,
    string SecondarySurfaceColor,
    string BorderColor);
