# Single Application Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Ausführung (2026-09-25):** von Claude Code selbst durchgearbeitet (`executing-plans`, ohne Subagenten) auf dem Branch `single-app-cleanup`. Alle Schritte abgehakt; der frische Review (Task 5, Schritt 5) fand keine Critical/Important-Befunde, vier kleine Punkte wurden behoben (Commit „Address cleanup review findings"). Die sichtbare Fahrprobe macht der Mensch; danach Merge nach `main`.

**Goal:** Make `PS4Manager.exe` the only startable application: the two former exe projects become libraries, every standalone-only code path disappears, and the repository is ready to be published.

**Architecture:** Keep the project graph and all names; only the packaging and the reachable code change. `src/FileManager.App` and `src/ScreenshotGallery.App` switch to `OutputType=Library` (their assemblies become `PS4FileManager.dll` / `PS4ScreenshotGallery.dll`), lose their entry points, and keep only the window classes the dashboard constructs. Everything the unified shell needs — dashboard, shared session, one transfer queue, prompts, migration — is untouched.

**Tech Stack:** C# 7.3, .NET Framework 4.8, WinForms, UIFramework sibling projects (published at `1330a53`), xUnit 2.9.2.

**Spec:** `docs/superpowers/specs/2026-09-25-single-app-cleanup-design.md`

**Branch:** work on a branch `single-app-cleanup` created from `main`; after the human's probe passes, merge it locally into `main` (house convention). No push.

## Global Constraints

- Target `net48` with `LangVersion` `7.3`; no syntax from newer C# versions.
- German comments in the app layer are the house style; keep them in German.
- `PS4Manager.exe` behaviour must not change: dashboard, one shared session, one transfer queue, tool windows, consolidated leave warnings, first-run migration.
- The legacy settings import keeps working: `LegacySettingsImporter` reads `%LocalAppData%\PS4FileManager\settings.json` and `%LocalAppData%\ScreenshotGallery\settings.json` via the constants in `src/PS4Manager.App/AppPaths.cs`. Do not touch those two files or that importer.
- Do not rename projects, folders, assembly names or namespaces.
- Do not modify UIFramework.
- The app layer has **no test project** (house-accepted gap). Per-task verification is therefore: the solution builds, the full suite stays green (543 tests), and targeted greps prove the dead path is gone. The visual confirmation is the human's probe.
- Unused `using` directives do not warn in this setup; removing them where they become obviously dead is fine, but not required.

---

### Task 1: Repository hygiene — track the plans, clear the private default

**Files:**
- Modify: `.gitignore` (root)
- Modify: `src/ScreenshotGallery.Core/Config/AppSettings.cs:5`
- Add to git: everything under `docs/superpowers/`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing that later tasks depend on; independent of the code changes.

- [x] **Step 1: Drop the ignore entry for the plan folder**

In `.gitignore` remove exactly this line:

```
docs/superpowers/
```

The file then reads:

```
bin/
obj/
TestResults/
.vs/
*.user
.superpowers/
.claude/
```

- [x] **Step 2: Clear the personal default address**

In `src/ScreenshotGallery.Core/Config/AppSettings.cs` replace

```csharp
        public string Ip { get; set; } = "10.10.10.236";
```

with

```csharp
        public string Ip { get; set; } = "";
```

Context: after this cleanup the gallery is a library inside the dashboard and reads no settings of its own; this type survives only as the shape of the legacy file that `LegacySettingsImporter` reads. `FileManagerSettings` and `ManagerSettings` already default to `""`.

- [x] **Step 3: Verify nothing else depended on that default**

Run:

```bash
grep -rn "10\.10\.10\.236" src/
```

Expected: no hit inside `src/`. (`tests/` may keep the literal as plain data — `Ps4.Core.Tests` builds FTP URIs with it.)

- [x] **Step 4: Update the two stale settings tests**

`tests/ScreenshotGallery.Core.Tests/SettingsStoreTests.cs` still asserts the old contract (“a missing or corrupt file yields the **PS4 defaults**”, meaning the fixture console's address). Replace both assertions, and rename the first test, so the file reads:

```csharp
        [Fact]
        public void A_missing_file_yields_empty_defaults()
        {
            // Diese Einstellungen sind nur noch die Gestalt der Alt-Datei, die
            // der Import beim ersten Start liest — eine Vorgabe-IP gibt es
            // deshalb nicht mehr (sie war die Adresse der Testkonsole).
            var loaded = SettingsStore.Load(_path);
            Assert.Equal("", loaded.Ip);
            Assert.Equal(2121, loaded.Port);
        }

        [Fact]
        public void A_corrupt_file_yields_the_defaults_instead_of_throwing()
        {
            File.WriteAllText(_path, "{ kaputt !!!");
            var loaded = SettingsStore.Load(_path);
            Assert.Equal("", loaded.Ip);
            Assert.Equal(2121, loaded.Port);
        }
```

- [x] **Step 5: Run the suite**

Run: `dotnet test ScreenshotGallery.sln -c Debug`
Expected: 543 passed, 0 failed.

- [x] **Step 6: Commit**

```bash
git add .gitignore src/ScreenshotGallery.Core/Config/AppSettings.cs \
        tests/ScreenshotGallery.Core.Tests/SettingsStoreTests.cs
git add docs/superpowers
git commit -m "Track the plans and clear the gallery's default address"
```

Expect `git status` afterwards to show no untracked file under `docs/superpowers/`. (Adding the whole folder is intentional: specs and plans travel with the repository from now on.)

---

### Task 2: The File Manager becomes a library with one code path

**Files:**
- Modify: `src/FileManager.App/FileManager.App.csproj`
- Delete: `src/FileManager.App/Program.cs`
- Delete: `src/FileManager.App/app.manifest`
- Delete: `src/FileManager.App/icon_file_manager.ico`
- Delete: `src/FileManager.App/AppPaths.cs`
- Modify: `src/FileManager.App/MainForm.cs`
- Modify: `src/FileManager.App/MainForm.Connection.cs`
- Modify: `src/FileManager.App/MainForm.Transfers.cs`

**Interfaces:**
- Consumes: `ManagerSessionController`, `ManagerSession`, `ManagerSettings` (from `PS4Manager.Core`); `ILeaveWarningSource` (`PS4Manager.Core.Presentation`).
- Produces: `public sealed partial class FileManagerForm : AppForm, ILeaveWarningSource` with exactly two ways in — the public constructor `FileManagerForm(ManagerSessionController sessions, ManagerSettings settings)` and its public members `event EventHandler ConnectionRequested`, `IReadOnlyList<string> CollectLeaveWarnings()`, `void ShowTransfers()`. `DashboardForm` (Task 3) keeps consuming these unchanged.

- [x] **Step 1: Make the project a library**

Replace the whole content of `src/FileManager.App/FileManager.App.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <AssemblyName>PS4FileManager</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Ps4.Core\Ps4.Core.csproj" />
    <ProjectReference Include="..\FileManager.Core\FileManager.Core.csproj" />
    <ProjectReference Include="..\PS4Manager.Core\PS4Manager.Core.csproj" />
    <ProjectReference Include="..\..\..\..\UIFramework\src\UIFramework.Core\UIFramework.Core.csproj" />
    <ProjectReference Include="..\..\..\..\UIFramework\src\UIFramework.Controls\UIFramework.Controls.csproj" />
    <ProjectReference Include="..\..\..\..\UIFramework\src\UIFramework.Grid\UIFramework.Grid.csproj" />
    <ProjectReference Include="..\..\..\..\UIFramework\src\UIFramework.Gallery\UIFramework.Gallery.csproj" />
    <ProjectReference Include="..\..\..\..\UIFramework\src\UIFramework.Tree\UIFramework.Tree.csproj" />
  </ItemGroup>
</Project>
```

(The application icon and manifest are gone; `assets/icon_file_manager.ico` stays as the dashboard's icon. `AssemblyName` stays `PS4FileManager` so the output is `PS4FileManager.dll`.)

- [x] **Step 2: Delete the standalone entry point and its paths**

```bash
git rm src/FileManager.App/Program.cs src/FileManager.App/app.manifest \
       src/FileManager.App/icon_file_manager.ico src/FileManager.App/AppPaths.cs
```

- [x] **Step 3: Strip the standalone state and lifecycle from `MainForm.cs`**

Remove these members (nothing else in the file changes):

| Member | Why |
|---|---|
| field `_legacySettings` | only the standalone settings file fed it |
| field `_ownedSessions` | the dashboard owns the session |
| fields `_sessionEndForCloseStarted`, `_closingAfterSessionEnd` | only the standalone close sequence |
| `internal FileManagerForm()` | standalone constructor |
| `static ManagerSettings FromLegacySettings(FileManagerSettings)` | only that constructor |
| `private void TrySaveSettings()` | the dashboard persists settings |
| `private async Task EndOwnedSessionAsync()` | the dashboard ends the session |
| `private async Task EndSessionAndCloseAsync()` | only the standalone close sequence |
| `protected override void OnFormClosing(FormClosingEventArgs)` | in the unified shell closing a tool window must neither ask nor end anything — the dashboard's consolidated leave check owns that |
| `private event Func<ExplorerSession, Task> SessionEndingAsync;` | its only caller was `EndOwnedSessionAsync`; leaving it would raise CS0067 (unused event) |

In `OnFormClosed` delete the single line `TrySaveSettings();`. The rest of that override stays — it still writes tree width and column widths into the shared `FileManagerViewSettings` and disposes the binding:

```csharp
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _settings.TreeWidth = _split.SplitterDistance;
            foreach (var column in _grid.Columns) _settings.ColumnWidths[column.Key] = column.Width;
            if (_sessionBinding != null) _sessionBinding.Dispose();

            base.OnFormClosed(e);
        }
```

Keep `_managerSettings` — `SetExpertMode`, `UpdateTargetText` and `AttachSessionOnUi` still use it. `Dispose(bool)` stays unchanged.

- [x] **Step 4: Reduce `MainForm.Connection.cs` to the shared-session surface**

Delete these members: field `_connectCts`, `private async Task ConnectAsync()`, `private async Task DisconnectAsync()`, `private bool EditConnection()`, `private bool ConfirmLeave(string)`, and the nested `LegacySessionFactory`.

Replace the `WireConnection()` body with:

```csharp
        partial void WireConnection()
        {
            // Gemeinsame Sitzung: Verbinden und Trennen führt nur das Dashboard;
            // hier wird lediglich darum gebeten (Dashboard-knopf „Connect"/„Select console …").
            _connectItem.Click += (s, e) => RaiseConnectionRequested();
            _targetItem.Click += (s, e) => RaiseConnectionRequested();
        }
```

`RaiseConnectionRequested()`, `CollectLeaveWarnings()`, `LoadQuickAccessAsync(...)`, `AttachSession(...)`, `AttachSessionOnUi(...)`, `InitializeAttachedSessionAsync(...)`, `DetachSession(...)`, `ApplyRequestedSessionOnUi()`, `DetachSessionOnUi(...)`, `IsCurrentSession(...)` and `RunSessionChangeOnUi(...)` all stay exactly as they are.

- [x] **Step 5: Drop the transfer warnings the dashboard now owns**

In `MainForm.Transfers.cs` delete the `SessionEndingAsync += async session => { ... }` registration (cancelling jobs and waiting for idle belongs to the dashboard) and the whole `CollectingCloseWarnings += warnings => { ... }` registration (transfers are global; `DashboardForm.CollectLeaveWarnings` reports them). The `SessionEnding += session => { ... }` registration and everything else stay.

`MainForm.Editing.cs` keeps its `CollectingCloseWarnings` registration — unsaved changes are tool-specific and reach the dashboard through `CollectLeaveWarnings()`.

- [x] **Step 6: Build and verify the dead path is gone**

```bash
dotnet build ScreenshotGallery.sln -c Release
grep -rn "_ownedSessions\|LegacySessionFactory\|FileManagerSettingsStore\|AppPaths" src/FileManager.App/
```

Expected: build succeeds with 0 warnings and 0 errors; the grep prints nothing.

- [x] **Step 7: Run the suite**

Run: `dotnet test ScreenshotGallery.sln -c Debug`
Expected: 543 passed, 0 failed.

- [x] **Step 8: Commit**

```bash
git add -A src/FileManager.App
git commit -m "Make the File Manager a library without a standalone path"
```

---

### Task 3: The Gallery becomes a library with one code path

**Files:**
- Modify: `src/ScreenshotGallery.App/ScreenshotGallery.App.csproj`
- Delete: `src/ScreenshotGallery.App/Program.cs`
- Modify: `src/ScreenshotGallery.App/MainForm.cs`
- Modify: `src/PS4Manager.App/DashboardForm.cs` (`BuildTool`, line 451; the gallery case is line 459-460)

**Interfaces:**
- Consumes: `ManagerSessionController`, `ManagerSession` (`PS4Manager.Core`), `MediaToolSession`, `MediaTransferPlanner`.
- Produces: `public sealed class MediaGalleryForm : SkinnedForm` with one constructor, `MediaGalleryForm(ManagerSessionController sessions)` — the `ManagerSettings` parameter is gone because the gallery reads no settings; `DashboardForm.BuildTool` must follow in the same task or the solution will not compile.

- [x] **Step 1: Make the project a library**

In `src/ScreenshotGallery.App/ScreenshotGallery.App.csproj` change

```xml
    <OutputType>WinExe</OutputType>
```

to

```xml
    <OutputType>Library</OutputType>
```

Leave everything else (assembly name `PS4ScreenshotGallery`, the project references, the WPF references for the video player) untouched. This project has no manifest and no icon.

- [x] **Step 2: Delete the standalone entry point**

```bash
git rm src/ScreenshotGallery.App/Program.cs
```

- [x] **Step 3: Collapse the constructors and `InitializeUi`**

In `src/ScreenshotGallery.App/MainForm.cs`:

1. Delete the parameterless constructor:

```csharp
        internal MediaGalleryForm()
        {
            var legacy = SettingsStore.Load(SettingsPath);
            ...
        }
```

2. Replace the unified constructor with:

```csharp
        public MediaGalleryForm(ManagerSessionController sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            InitializeCaches(Path.Combine(UnifiedDataRoot, "cache", "gallery"));
            InitializeUi();
            _mediaToolSession = CreateMediaToolSession(_sessions);
        }
```

3. Change `private void InitializeUi(bool standalone)` to `private void InitializeUi()` and delete the standalone branch so the method starts like this:

```csharp
        private void InitializeUi()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "PS4 Screenshot Gallery";
            ClientSize = new Size(1100, 700);
            StartPosition = FormStartPosition.CenterScreen;

            var toolbar = new SkinPanel { Dock = DockStyle.Top, Height = 44 };
            _refresh.Location = new Point(8, 8);
            _status.Location = new Point(118, 12);
            _status.AutoSize = true;
            toolbar.Controls.Add(_refresh);
            toolbar.Controls.Add(_status);
```

Do **not** paste a `...` placeholder: insert exactly the lines above and keep the rest of the existing method body unchanged after `toolbar.Controls.Add(_status);` (gallery sizing, `ItemImageNeeded`, the right-click menu, `ItemDoubleClick`, `Controls.Add(_gallery)`, `Controls.Add(toolbar)`).

4. Delete the now-dead line at the end of `InitializeUi`:

```csharp
            _connect.Click += async (s, e) => await ConnectStandaloneAsync();
```

The sibling `_refresh.Click += (s, e) => _mediaToolSession.Reload();` stays.

- [x] **Step 4: Delete the standalone fields, classes and method**

In `src/ScreenshotGallery.App/MainForm.cs` delete:

| Member | Why |
|---|---|
| fields `_ip`, `_port`, `_connect` | the toolbar branch that owned them is gone |
| field `_settings` and both of its assignments | after the standalone path is gone nothing reads settings (verified: the only readers were the toolbar and `ConnectStandaloneAsync`) |
| field `_ownedSessions` | the dashboard owns the session |
| constants `LegacyDataRoot`, `SettingsPath` | paths of the standalone settings file; the dashboard's migration reads that file by path |
| `private static string LegacyDataRoot …` / `private static string SettingsPath …` | same |
| `private async Task ConnectStandaloneAsync()` | the dashboard connects |
| nested `LegacySessionFactory` | unreachable |
| nested `GalleryPrompts` | only the legacy factory used it; prompts of the shared session come from the dashboard |
| nested `ReplaceConflictResolver` | only the legacy factory used it; the shared queue resolves conflicts through the dashboard's dialog |
| the `_ownedSessions != null` block in `OnFormClosed` | nothing to disconnect |

Keep `UnifiedDataRoot`, `InitializeCaches(string)`, `CreateMediaToolSession(...)`, the attach/detach callbacks, `Root(...)`, and all operation code.

After deleting, the `using ScreenshotGallery.Core.Config;` and `using Ps4.Core.Ftp;` directives may become unused — harmless, remove them only if you are sure nothing else in the file needs them.

- [x] **Step 5: Follow the new constructor in the dashboard**

In `src/PS4Manager.App/DashboardForm.cs` (`BuildTool`) change

```csharp
                case MediaKey:
                    return new MediaGalleryForm(_sessions, _settings);
```

to

```csharp
                case MediaKey:
                    return new MediaGalleryForm(_sessions);
```

- [x] **Step 6: Build and verify the dead path is gone**

```bash
dotnet build ScreenshotGallery.sln -c Release
grep -rn "_ownedSessions\|LegacySessionFactory\|ConnectStandaloneAsync\|LegacyDataRoot\|GalleryPrompts\|ReplaceConflictResolver" src/ScreenshotGallery.App/
grep -rn "MediaGalleryForm(" src/
```

Expected: build succeeds with 0 warnings and 0 errors; the first grep prints nothing, the second prints only the `new MediaGalleryForm(_sessions)` call in `src/PS4Manager.App/DashboardForm.cs`.

- [x] **Step 7: Run the suite and check the output shape**

```bash
dotnet test ScreenshotGallery.sln -c Debug
ls src/FileManager.App/bin/Release/net48/*.exe src/ScreenshotGallery.App/bin/Release/net48/*.exe
```

Expected: 543 passed, 0 failed; the `ls` reports “No such file” for both — the libraries no longer produce executables.

- [x] **Step 8: Commit**

```bash
git add -A src/ScreenshotGallery.App src/PS4Manager.App/DashboardForm.cs
git commit -m "Make the gallery a library without a standalone path"
```

---

### Task 4: Documents describe the single application

**Files:**
- Modify: `README.md`
- Modify: `docs/ps4-manager-migration.md`

**Interfaces:**
- Consumes: the state produced by Tasks 1–3 (one executable, libraries).
- Produces: user-facing build, launch and migration text that matches the code.

- [x] **Step 1: Rewrite the README's application sections**

`README.md` currently describes three applications. Change it so it describes one, section by section:

1. Replace the intro paragraph and the `## PS4 Manager (empfohlener Einstieg)` heading block with a single lead section that names the executable first:

```markdown
# PS4 Manager

`PS4Manager.exe` ist die Anwendung: ein Dashboard, das die Verbindung zu einer
jailbroken PS4 über den FTP-Server von **GoldHEN** besitzt und die beiden
Werkzeuge als eigene Fenster öffnet — den **File Manager** (Dateien wie im
Windows Explorer) und **Screenshots & Videos** (Galerie mit Vorschau und
Player). Beide teilen sich **eine** FTP-Verbindung, **eine** Warteschlange und
**einen** Satz Einstellungen: Ein Download, der in der Galerie beginnt, ist auch
im File Manager sichtbar — und umgekehrt.
```

2. Keep the bullet list that follows it (Verbinden, Werkzeuge, Übertragungen, Beenden, noch nicht gebaute Module) — it already describes the dashboard correctly. Delete the sentence pointing at the migration document from this spot only if it is duplicated below; it must remain exactly once.

3. Delete the whole `## Screenshot Gallery (Einzel-App)` block, including its `### Funktionsumfang` list, and fold the still-true bullets into a new `## Werkzeuge` section with two subsections:

```markdown
## Werkzeuge

### File Manager
- **Verbinden:** „Select console …" im File Manager bittet das Dashboard um die
  Verbindung; verbunden und getrennt wird immer dort.
- **Links** Schnellzugriff (GoldHEN, Plugins, Cheats, PKG-Ablage, Spiele,
  Spielstände, USB, angeheftete Ordner) und der Ordnerbaum der Konsole,
  **rechts** Details oder Kacheln. Spielordner zeigen den Spielnamen.
- **Übertragen wie im Explorer:** Ziehen und Strg+C/Strg+V in beide Richtungen
  mit dem Windows Explorer. Innerhalb der PS4 verschieben per Ziehen oder
  Strg+X/Strg+V.
- **Öffnen mit Rückschreiben:** `.ini`-Dateien öffnet der eingebaute Editor,
  andere Dateien das zugeordnete Windows-Programm; nach dem Speichern fragt der
  File Manager, ob die Änderung zurück auf die PS4 soll.
- **Schutzzonen:** frei (`/data`, USB-Sticks, Aufnahmen), Warnung (Spiele,
  Spielstände, Patches, Zusatzinhalte), alles andere gesperrt. Der
  Expertenmodus stuft „gesperrt" zu „Warnung" herab.
- **Die PS4 hat keinen Papierkorb** — Löschen ist endgültig.

### Screenshots & Videos
- Screenshots und Videos liegen in derselben Spielgruppe, gemeinsam nach
  Aufnahmezeit sortiert; Clips sind am Play-Zeichen zu erkennen.
- **Player:** Doppelklick lädt und spielt einen Clip — Play/Pause (Leertaste),
  Positionsregler, ±10 s über Umschalt+Pfeil, Links/Rechts blättert.
- **Mehrfachauswahl** über das Kontextmenü: Herunterladen, in die
  Zwischenablage kopieren, Vorschau öffnen, Löschen (Datei, `.dat` und
  Thumbnail).
- **Lokaler Cache** für Vorschaubilder, Vollbilder und Clips.
```

4. Replace the `## Bauen` section body after the test command with:

```markdown
Die Solution enthält eine Anwendung — `PS4Manager.exe` — und sechs
Bibliotheken: `PS4Manager.Core` (gemeinsame Sitzung, Einstellungen, Migration),
`FileManager.Core` (Dateimanager-Kern), `ScreenshotGallery.Core` (Galerie:
Medienerkennung, Vorschaubilder, Caches), `Ps4.Core` (FTP und Titel-Infos),
`FileManager.App` (Fenster des File Managers) und `ScreenshotGallery.App`
(Fenster der Galerie). Die beiden letzten sind Bibliotheken, keine Programme:
`PS4FileManager.exe` und `PS4ScreenshotGallery.exe` gibt es nicht mehr.

Nach dem Bauen liegt die Anwendung unter:

```text
src/PS4Manager.App/bin/Release/net48/PS4Manager.exe
```
```

5. Replace the `## Lokale Daten` section's second paragraph (the one starting `**Screenshot Gallery (Einzel-App)**`) with:

```markdown
**Die früheren Einzel-Apps** gibt es nicht mehr; ihre Datenordner
`%LocalAppData%\ScreenshotGallery` (Einstellungen, Thumbnail-Cache) und
`%LocalAppData%\PS4FileManager` (Einstellungen, Arbeitskopien) bleiben beim
ersten Start des Managers als Import-Quelle liegen und werden nicht mehr
beschrieben.
```

6. Delete the trailing `## PS4 File Manager` section (its content moved into `## Werkzeuge`) and keep the `## Hinweis zur PS4-Galerie-Datenbank` section unchanged.

- [x] **Step 2: Correct the migration document**

In `docs/ps4-manager-migration.md` replace

```markdown
Die Einzel-Apps schreiben weiterhin in ihre eigenen Ordner
(`%LocalAppData%\PS4FileManager`, `%LocalAppData%\ScreenshotGallery`); solange
sie zusätzlich benutzt werden, laufen beide Datenbestände nebeneinander.
```

with

```markdown
Die früheren Einzel-Apps gibt es nicht mehr. Ihre Ordner
(`%LocalAppData%\PS4FileManager`, `%LocalAppData%\ScreenshotGallery`) bleiben
als Import-Quelle liegen; geschrieben wird nur noch nach
`%LocalAppData%\PS4Manager`.
```

Everything else in that document stays — the import rules, the paths and the
“what is not migrated” list are still accurate.

- [x] **Step 3: Verify the documents contain no stale references**

```bash
grep -rn "PS4FileManager.exe\|PS4ScreenshotGallery.exe\|Einzel-App" README.md docs/*.md
```

Expected: hits only in sentences that explicitly say those executables/programs no longer exist.

- [x] **Step 4: Commit**

```bash
git add README.md docs/ps4-manager-migration.md
git commit -m "Describe the single application in README and migration notes"
```

---

### Task 5: Full verification, review, and probe handover

**Files:**
- Modify: whichever file a confirmed review finding names.

**Interfaces:**
- Consumes: everything above.
- Produces: the evidence the human needs for the probe.

- [x] **Step 1: Rebuild the release output from scratch**

```bash
rm -rf src/PS4Manager.App/bin src/PS4Manager.App/obj
dotnet build ScreenshotGallery.sln -c Release
```

Run: `ls src/PS4Manager.App/bin/Release/net48/`

Expected: `PS4Manager.exe`, `PS4Manager.Core.dll`, `FileManager.Core.dll`,
`ScreenshotGallery.Core.dll`, `Ps4.Core.dll`, `PS4FileManager.dll`,
`PS4ScreenshotGallery.dll`, the UIFramework assemblies and `.config`/`.pdb`
files. In particular **no** `PS4FileManager.exe` and **no**
`PS4ScreenshotGallery.exe`.

- [x] **Step 2: Run the complete suite**

```bash
dotnet test ScreenshotGallery.sln -c Debug
dotnet test ScreenshotGallery.sln -c Release
dotnet build ScreenshotGallery.sln -c Release --no-restore
git diff --check
```

Expected: 543/543 in both configurations, 0 warnings and 0 errors in the build, no whitespace errors.

- [x] **Step 3: Smoke the executable hidden**

Start `src/PS4Manager.App/bin/Release/net48/PS4Manager.exe` with a hidden window, wait four seconds, assert it has not exited and reports `Responding`, then stop exactly that process. Expected: PASS.

- [x] **Step 4: Grep for leftovers across the repository**

```bash
grep -rn "PS4FileManager.exe\|PS4ScreenshotGallery.exe" src/ tests/ README.md docs/
grep -rn "_ownedSessions\|LegacySessionFactory\|ConnectStandaloneAsync" src/
```

Expected: the first grep only in `README.md`/`docs/` where the text says those programms no longer exist; the second prints nothing.

- [x] **Step 5: Ask for a fresh read-only review**

Dispatch a reviewer (read-only; explicitly forbid GUI automation, input simulation, screenshots and starting applications) over the whole cleanup diff with the spec at hand. Focus: any remaining second code path into a connection, queue or settings file; anything that still constructs an `FtpClient` outside `ManagerSessionFactory`; migration still able to read the legacy files; documentation claims that the code no longer honours. Fix every confirmed Critical, Important or Minor finding and repeat the affected steps of this task.

- [x] **Step 6: Hand the probe to the human**

Report, as a checklist: start `PS4Manager.exe`; connect; open both tool windows; a transfer that shows up in the dashboard summary; close a tool window (the dashboard stays connected); close the dashboard (one consolidated warning at most); check that the status bar text of the File Manager is no longer clipped; check the release folder holds no second executable. The visible clicks stay with the human.

- [x] **Step 7: Commit any review fixes**

```bash
git add -A
git commit -m "Address cleanup review findings"
```

Only if Step 5 changed anything.

---

## After this plan

Write the GoldHEN Manager spec and plan next (the human's order: cleanup first, then GoldHEN Manager). The dashboard's GoldHEN tile currently opens a `PhaseStatusForm`; that plan replaces it.
