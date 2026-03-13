# Skill: XAML Regex Gates

Use regular expressions to verify XAML structure and content without launching the application. This allows for fast, automated verification of UI constraints and design rules.

## Context

When verifying UI changes in CI or environments where launching the full app is difficult or slow, or when enforcing strict design rules (e.g., "no status pills in title bar").

## Pattern

1.  **Read XAML as Text**: Load the `.xaml` file content as a string.
2.  **Target Specific Elements**: Use Regex to extract the body of specific controls (e.g., `<Grid x:Name="TitleBarDragRegion"...>`).
3.  **Assert Content**: Verify that the extracted body contains (or does not contain) expected strings, bindings, or properties.

## Example

```csharp
[TestMethod]
public void TitleBar_IsClean()
{
    var xaml = File.ReadAllText("Views/MainPage.xaml");
    var titleBar = Regex.Match(xaml, """<Grid x:Name="TitleBarDragRegion"(?<body>.*?)</Grid>""", RegexOptions.Singleline);
    
    Assert.IsTrue(titleBar.Success);
    Assert.IsFalse(titleBar.Groups["body"].Value.Contains("StatusPill"), "Title bar should not contain status pills.");
}
```

## Benefits

-   **Speed**: Runs in milliseconds as a unit test.
-   **Stability**: Does not flake due to rendering timing or window focus.
-   **Enforcement**: Can prevent "drift" where developers accidentally re-add clutter.
