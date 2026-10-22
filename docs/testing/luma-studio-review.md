# Luma Studio v1 verification

Independent read-only review: Astra medium, db7511b..b62b02f. No Critical findings;
two Important findings reproduced and fixed with failing-then-passing regressions.

- **Real net48 socket timeout:** a loopback listener accepts and remains silent.
  Both the request timeout and caller cancellation now close the captured connection,
  normalize the resulting I/O exception and invalidate the Studio session. Fake
  streams alone did not expose this platform behavior.
- **Upload destination:** a GTA profile/RDR port mismatch is rejected before
  authentication. Immutable upload plans bind the endpoint/filename/generation
  to the confirmation and actual relative PUT target. The expected absolute
  sandbox is explicitly unverified because current Luma HELLO has no game identity.
- **Release gating:** tags require hosted source/core/server acceptance before
  desktop build/tests, package verification and private release publication.
  Native Lua/Scintilla x86 tests run separately from the full desktop suite.

Latest local Release verification: 1,062 passed, zero failed/skipped. An additional
29 Studio/core/editor tests passed in x86. Both Lua architectures built from
5.4.7 source with static C runtimes. The 34-file package allowlist includes the
Scintilla runtime subdirectories, Lua checker DLLs and third-party notices;
fixture tests verify missing dependencies, exclusive archive creation, hashes,
closed handles and exclusion of arbitrary credentials/test DLLs.

Offline dark-theme rendering was inspected. Regression tests cover independent
buffers, cancelled close, dashboard shutdown veto, real line diagnostics,
stale compilation suppression, unique runtime-error provenance, source conflicts,
bounded project traversal, authentication, upload framing/path validation,
separate actions and connection cancellation/reconnect.

No live PS4 connection, upload, execution or reload was performed. A real-console
smoke test remains for the user. DevExpress currently reports trial diagnostics;
the release is private owner evaluation only.

Deferred Minor: only the last Execute retains clickable runtime provenance.
Errors from earlier callbacks remain console text; they are never attributed
to a newer buffer. No other reviewer behavior was declined or left unassessed.

## Published verification

Private preview: https://github.com/HandsomeHeise/PS4Manager/releases/tag/v0.2.0-luma-studio-preview

Tag workflow 39725794209 passed all three jobs (hosted core, hosted server,
desktop). The actual downloaded 34-file release asset was extracted and smoke
tested under both x64 and x86: native Lua, DevExpress Studio and Scintilla/Lexilla
loaded successfully without connecting to a console.

Asset SHA256, verified against GitHub's published digest:
`1808dcfb51dc724d8cfbc0252e2ea205e702d6180b2ffe0709cda13c4f4a156c`.
