# WinUI 3 unpackaged F5

## Pattern

For a WinUI 3 desktop app that should F5 cleanly in Visual Studio without Store/MSIX friction:

1. Keep `WindowsPackageType` set to `None`.
2. Disable `EnableMsixTooling` for `Debug` so everyday startup behaves like a normal desktop exe.
3. Make the only/default `launchSettings.json` profile use `"commandName": "Project"`.
4. Leave packaging available in non-Debug configurations if you still expect a later MSIX publish lane.

## Why it works

- New developer machines inherit the unpackaged startup profile automatically.
- Debugging stops depending on package registration or Store-style tooling.
- You do not give up future shipping options; you just stop making them the day-to-day inner loop.

## When to use it

- Windows App SDK / WinUI 3 apps that need a low-friction contributor setup.
- Teams that still want an MSIX story for release, but not for every F5.
