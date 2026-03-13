# WinUI unpackaged smoke verification

## When to use

Use this when a Windows App SDK / WinUI desktop app is supposed to run unpackaged from Visual Studio F5 and you need evidence stronger than "the project built."

## Steps

1. Verify project configuration:
   - `WindowsPackageType` is `None`
   - Debug configuration disables MSIX tooling if Visual Studio packaging prompts are undesirable
   - `Properties/launchSettings.json` contains the unpackaged project profile
2. Run a clean repo validation:
   - `dotnet build`
   - `dotnet test`
3. Smoke the actual built exe:
   - launch the unpackaged exe
   - wait briefly and confirm the process stays alive
   - capture the main window title if possible
   - if screen-copy capture returns a black image, fall back to a Win32 `PrintWindow` capture of the app hwnd
   - verify first-run artifacts the app is expected to create
4. Stop the process cleanly after the check.

## What this proves

- The app can start unpackaged on the current machine.
- The intended F5 path is not blocked by MSIX/Store packaging requirements.
- Startup initialization actually runs, not just compilation.

## What this does not prove

- Cold-machine readiness on a device without the Windows App Runtime installed.
- End-to-end feature behavior beyond startup unless additional scripted checks are added.
