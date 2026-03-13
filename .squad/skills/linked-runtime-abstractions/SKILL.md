---
name: "linked-runtime-abstractions"
description: "Keep a WinUI app buildable when it cannot yet reference a sibling runtime abstractions project directly"
domain: "windows-app"
confidence: "high"
source: "bishop"
---

## Context

Use this when the Windows app and runtime/integration lane need to share interfaces, but target-framework or packaging constraints prevent a normal project reference.

## Patterns

### Link the whole contract tree, not a hand-picked file list
- In the app `.csproj`, include the entire abstractions source tree with a recursive glob.
- Exclude `bin` and `obj` explicitly.
- Preserve folder structure with `%(RecursiveDir)` so generated paths stay readable in Solution Explorer.

```xml
<Compile Include="..\WorkIQC.Runtime.Abstractions\**\*.cs"
         Exclude="..\WorkIQC.Runtime.Abstractions\bin\**\*;..\WorkIQC.Runtime.Abstractions\obj\**\*">
  <Link>RuntimeAbstractions\%(RecursiveDir)%(Filename)%(Extension)</Link>
</Compile>
```

### Match local adapters to the current contract shape
- If the app keeps placeholder adapters for shell stability, update them whenever contract return types change.
- Prefer structured readiness/result objects over temporary booleans so the shell can degrade gracefully without hiding missing capabilities.

### Treat XAML compiler failures as possible upstream C# failures
- WinUI build output often reports the XAML compiler exit code last.
- Re-run the solution build at minimal verbosity and scan for earlier C# contract errors before changing XAML.

## Anti-Patterns

- Hand-maintaining a partial linked-file list for a fast-moving contract project.
- Returning placeholder `bool` values once the contract has moved to structured result objects.
- Assuming a WinUI XAML compiler failure means the XAML itself is broken.
