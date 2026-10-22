# DevExpress migration acceptance — 2026-10-22

User request: remove the UIFramework dependency throughout the Windows client and adapt the supplied dark Ribbon dashboard reference to PS4.

## Result

All three client UI assemblies and the main application use DevExpress WinForms 25.2.4. PS4Manager.UI provides native control bindings and application row/media/navigation models. No old framework source references, project references or runtime assemblies remain in the client. The dashboard has a dark PS4 banner, PlayStation-blue accents, Ribbon pages, four summary groups, console connection, games and mod shortcuts. Native light theme, Settings/Frame4 toggle, dialogs and non-activating desktop notification cards are supported.

The source snapshot before migration is retained locally. The original Git directory under E:/Projects/PS4/ScreenshotGallery is unavailable, so this work cannot be committed through the old managed-worktree metadata. No Git state was reset or recreated. A portable client source archive accompanies the test build.

## Verification

- Full Release rebuild succeeds with **0 compiler errors**. After adding the official license generator, the installed license produces **8 DX1000/DX1001 evaluation-license warnings**. These are not suppressed.
- Final complete solution test run after adding that generator: **1,031 passed**, no failures or skips: 532 manager, 361 file manager, 96 PS4 core, 42 gallery. TRX results are under artifacts/devexpress-licensed-build-check/.
- Eleven additional migration cases cover native Ribbon layout in both themes and at minimum width, old-reference removal, sorted-row identity and edit commit, multi-selection, tree identity under asynchronous child replacement, gallery image completion on the UI thread, adjacent-row drag source identity, name type-ahead from another column, clickable breadcrumbs/direct Unix-path entry, and actual editor Enter preprocessing to the form's default button.
- Dark/light and 1080/1440-width dashboard images were generated and visually inspected. Native monitor DPI changes were not exercised; existing software-scale UI tests passed.
- Independent read-only review found no Critical issues. Its Important tree drag-source finding and Minor missing type-ahead finding were fixed and tested. Re-review found no additional supported Critical/Important issues.
- Source comparison with the pre-migration archive: **197 core/protocol source/project files unchanged**. No console access or mutation was performed.
- Packaging fixture passed: missing inputs, exclusive output, fixed runtime manifest, per-entry length/SHA-256 verification and closed archive handles. All referenced DevExpress assemblies are in scripts/client-runtime.json.
- Test ZIP: `PS4Manager-DevExpress-2026-10-22-Test.zip`, 26 files, 33,841,409 bytes, SHA-256 `927BA0F24D729ADBEE2D347EE9190A001E162B456C01AFCC6286C40ED9128FF1`. Verified: no old framework files, developer license files, settings or credentials.

## License and remaining acceptance

This machine's official DevExpress generator reports TRIAL/missing eligible product entitlement. The build is for local evaluation, not a publicly distributable release. Register a valid WinForms/DXperience/Universal license through the installer and rebuild before distribution. The private license file was not read, embedded manually, copied or packaged. Build-time licensing follows [DevExpress's 25.2 instructions](https://docs.devexpress.com/GeneralInformation/405494/trial-register/set-up-your-dev-express-license-key?v=25.2).

Hardware acceptance of the migrated UI remains to be done by the user: connect, browse, transfer, install/recover and exercise trainer actions after a fresh pause/confirmation. Existing automated protocol and mutation-safety tests passed, but do not substitute for a real-console UI run. This migration did not alter live protection or enable/disable Godmode.
