# Editor reference redesign

Implemented the user's supplied workstation screenshot as the Luma Studio visual
direction: #181B21 editor, #1E2229 sidebar, #5BADFF active-tab rule, purple Lua
keywords, amber numbers, blue function names, flat document tabs and source tree.
The code editor now owns the full right workspace width. API help moves into the
bottom tool strip alongside Output, Problems, Live diagnostics, Search and Navigation.
The footer follows the current caret, UTF-8/Lua mode and current diagnostic state.
Ctrl+F and F1 reveal the appropriate bottom tools; navigation switches actual buffers.
Existing Check/Execute/Upload/Reload and all document/session safeguards are retained.

Six new UI cases watched fail before implementation: three window sizes, local
palette/global-theme isolation, caret/document navigation, and large-font command
button sizing. Existing Studio tests continue to pass. Latest complete Release
suite: 1,068 passed (569 Manager, 361 FileManager, 96 Ps4, 42 Gallery). An additional
11 Studio UI/design cases passed under x86. Screenshots were inspected during
iteration; final local capture is artifacts/luma-studio-reference.png.

One independent read-only Astra review found no Critical/Important issues. Its
minor DPI preferred-size finding was reproduced by a larger-font regression and
fixed. Actual multi-monitor DPI transitions and physical close-button interaction
were inspected in source, not exercised on hardware. Native close-hit regions are
retained; only their painting changes. No console connection was made.

Local package: artifacts/PS4Manager-Windows-editor-reference.zip, 34 allowlisted
runtime entries. SHA256:
`2bc8b073525ea1195de189b4622eedea51ba56b18cf31ea83a66de56fc86fb16`.
