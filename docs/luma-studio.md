# Luma Studio v1

Open **Luma Studio** from the dashboard's PS4 / Modding tab. Local editing and
syntax checks work without a console. No connection is made automatically.

## Workflow

1. Choose GTA V or RDR2, then open Lua files or a local project folder. Templates
   create independent unsaved buffers. Multiple tabs retain independent edits.
2. **Check** compiles Lua 5.4.7 text locally without executing it. Checking is also
   debounced while typing. Double-click a diagnostic to navigate to its line.
3. For live operations, start the matching game with Luma active, close other Luma
   control clients (including VS Code), enter the console address/port/token and
   press **Connect**. V1 requires GTA V on 9615 or RDR2 on 9616; custom forwarded
   ports are not supported. The current protocol cannot report the game's identity.
4. **Execute** runs the current buffer once, including unsaved edits. **Upload**
   writes the current buffer to a simple `.lua` filename under the game's scripts
   directory. Confirmation names the captured console endpoint, selected game,
   exact relative replacement path and explicitly unverified expected sandbox.
5. Upload does **not** reload or execute. Press **Reload** separately when ready.
   It reloads the game's scripts, not just the current editor tab.

GTA V normally uses `/data/gtalua/scripts`; RDR2 normally uses `/data/rdr2lua/scripts`.
PUT sends the relative `scripts/<filename>` path, resolved by the connected plugin
under its own game's sandbox. Confirm the correct game is running: the client
does not claim to verify that root from HELLO. An upload
can replace an existing script, so keep a local copy. Choose Save as to rename
an unsaved file or a remote target. Lua upload filenames allow ASCII letters,
digits, underscore, dash and dots; no directories or traversal.

## Editor

The workstation layout keeps the project explorer on the left and the code editor
wide. Output, Problems, Live diagnostics, Search, Navigation and API help live in
the bottom tool strip. The Studio has its own dark reference palette without
changing the dashboard's light/dark preference. The footer shows line/column,
encoding and current syntax status.

- Ctrl+N / Ctrl+O: new/open; Ctrl+S: save; Ctrl+W: close tab.
- Ctrl+F / Enter or Next: find; F5: local syntax check (not Execute).
- Ctrl+Space: API completion; `(`: signature; F1: symbol documentation.
- Folding margin collapses Lua blocks; indentation uses four spaces.
- GTA V and RDR2 use their actual Luma runtime/native definitions and templates.
  Syntax success does not prove native availability, argument types or runtime safety.

Files use strict UTF-8, preserving an existing BOM. Saving detects external file
changes and asks before replacing them. Closing prompts for unsaved edits.
Projects scan at most 1,000 Lua files, 5,000 entries and 12 levels, excluding links and build/hidden
folders. Source checks/uploads are limited to 1 MiB. Console output is bounded to
1,500 timestamped lines; runtime errors from the last executed current-version
buffer can be navigated in Diagnostics. Older-buffer errors remain console text.

The control connection serializes commands and log polling. Disconnect/reconnect
and profile changes invalidate pending work. There is no v1 debugger, breakpoint
support, remote file browser or semantic type checker.

## Building

Install .NET Framework 4.8 developer tools, a Windows C toolchain (VS 2022/2026),
CMake, .NET SDK and DevExpress WinForms 25.2.4. Then:

```powershell
./scripts/build-lua.ps1
dotnet build PS4Manager.sln -c Release
dotnet test PS4Manager.sln -c Release --no-build
./scripts/package-client.ps1 -OutputPath artifacts/PS4Manager-Windows.zip
```

The checker builds x86/x64 from the vendored Lua source with a static C runtime.
Scintilla5.NET is restored from NuGet; its architecture-specific runtimes must stay
in the `runtimes` subfolders of the ZIP. Extract the whole archive, not just the EXE.
See THIRD-PARTY-NOTICES.txt. DevExpress trial builds are private evaluation only.
