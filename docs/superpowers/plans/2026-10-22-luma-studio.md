# Luma Studio Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline; the user explicitly requested autonomous completion without a review pause.

**Goal:** Ship a private source repository with working CI/releases and an integrated Lua/Luma editor.

**Architecture:** DevExpress hosts a Scintilla editor; a native compile-only Lua 5.4.7 checker and pure core document/API/session models support the UI. A dedicated local Windows runner builds proprietary UI dependencies without committing them.

**Tech Stack:** .NET Framework 4.8, C# 7.3, DevExpress 25.2.4, Scintilla, Lua 5.4.7 C sources, xUnit, GitHub Actions and Node 24 server.

**Spec:** docs/superpowers/specs/2026-10-22-luma-studio-design.md

## Global Constraints

- Source-only private repository; no secrets, developer keys, proprietary DLLs or compiled native binaries in Git.
- Explicit local-evaluation labeling while DevExpress reports trial status.
- No live-console development actions.
- Check, Execute, Upload and Reload remain separate.

## Review Focus

- Dirty documents and external file changes must not lose user edits.
- A delayed check or log must not modify a new document/session.
- Upload paths and overwrite prompts must remain bound to the selected game/profile.
- Syntax checking must not execute scripts or accept binary chunks.
- Packaged x86/x64 native dependencies and CI artifacts must match the executable.

### Task 1: Private repository and CI

Files: .github/workflows/checks.yml, desktop.yml, docs/github-builds.md, .gitignore.
- [ ] Scan a clean copy of the latest source; create/verify private GitHub repository and commit baseline.
- [ ] Register a dedicated local runner, push, and verify hosted/source and desktop build jobs.
- [ ] Create a version tag and verify its private evaluation release.

### Task 2: Checker, API and document core

Files: native/lua54/*, scripts/build-lua.ps1, src/PS4Manager.Core/Luma/Studio/*, tests/PS4Manager.Core.Tests/Luma/Studio/*.
Interfaces: LuaSyntaxChecker.Check(string source, string fileName); LuaApiCatalog.ForProfile(string profile); StudioDocument.Load/Save with source/version/dirty state; compile diagnostics carry document path and line.
- [ ] Write tests for Lua 5.4 constructs, compile-only behavior, bytecode/size rejection, UTF-8 and line errors, profile separation and safe file save conflicts.
- [ ] Implement bounded compile-only native checking, definition parsing, templates and document/project models; verify tests and commit.

### Task 3: Serialized Luma operations

Files: src/PS4Manager.Core/Luma/Studio/StudioConnection.cs, LumaClient.cs, tests/Luma/Studio/*.
Interfaces: ConnectAsync, ExecuteAsync, UploadAsync, ReloadAsync, PollAsync, Disconnect; log/diagnostic events carry the session generation.
- [ ] Test authentication, PUT framing/path validation, separation of operations, cancellation/reconnect and stale output with fake protocol transports.
- [ ] Implement, verify and commit.

### Task 4: Studio UI

Files: src/PS4Manager.App/LumaStudio/*, DashboardForm*.cs, PS4Manager.App.csproj, scripts/client-runtime.json.
- [ ] Integrate Scintilla into DevExpress tabs/tree, toolbar, diagnostics, console and API help.
- [ ] Add new/open/save/project, close prompts, debounced compile, completion/call tips/folding/find, templates and keyboard navigation.
- [ ] Connect separate actions to the tested core, replace dashboard Luma entry with the Studio, and verify UI/session lifecycle with deterministic tests.
- [ ] Commit the working Studio.

### Task 5: Review, package, release

- [ ] Complete solution/server checks, offline UI rendering and runtime dependency verification.
- [ ] Obtain one independent review, fix supported blockers and record remaining limits.
- [ ] Push the completed source, create a preview tag, verify private CI/release and deliver the checked ZIP.
