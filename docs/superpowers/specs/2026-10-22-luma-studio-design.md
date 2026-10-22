# Luma Studio v1

User-approved scope: a Lua/Luma IDE inside PS4Manager, built autonomously after creating a private source-only GitHub repository with automatic builds/releases.

Keep the DevExpress Windows shell and .NET Framework 4.8. Integrate a native Scintilla editor for Lua highlighting, line numbers, indentation, folding, multiple document tabs and completion. Add a local project tree, new/open/save/save-as, dirty-file close prompts, basic find and keyboard shortcuts. Closing or switching a tab never silently discards edits. Project traversal is bounded and does not follow directory links.

Syntax checking compiles text only with the same Lua 5.4.7 source used by the local Luma project. It never executes user code on the PC. Build native x86/x64 checker DLLs from vendored MIT-licensed source; keep only sources/licenses in Git. Bound input size, reject bytecode, return file/line diagnostics, debounce checking and discard stale results. Errors are clickable and focus the correct document/line.

Use the local Luma editor definitions as the API source, with their MIT notice. Profile-specific GTA V (CUSA00411/1.57) and RDR2 (CUSA03041/1.32) symbols, signatures and documentation feed completion/help. Include Lua standard functions. Do not invent native APIs or infer semantic correctness from a successful parse. Supply hello/log/tick examples and profile-appropriate native examples.

Keep actions separate: Check, Execute current buffer, Upload current file, Reload scripts. Connect to Luma through its existing authenticated control protocol. Upload uses the existing PUT operation into the selected profile's scripts directory, with explicit overwrite approval; it does not reload automatically. Execute is blocked for syntax errors, unsaved editor changes are executed as the current buffer intentionally, and console output/runtime errors are bounded and displayed beside diagnostics. Idle polling drains Luma logs through serialized protocol requests. Disconnect, profile changes, closing and reconnect cancel/invalidate old work. Luma Studio and the old manager must not occupy the plugin's one-client slot simultaneously.

No autonomous PS4 connection, file writes, execution or reload during development. Use fake transports, loopback and UI tests. Package the tested Windows build, commit/push source, and verify the configured GitHub jobs and private prerelease.
