# PS4Manager DevExpress migration

Requested: remove UIFramework completely and use the supplied XboxStudio dashboard as the visual reference, adapted to PS4.

Use the installed DevExpress WinForms 25.2, retain .NET Framework 4.8 and C# 7.3. All app windows use XtraForm, native DevExpress editors, tabs, splitters, grids, tree and gallery. A small shared PS4Manager.UI project holds application bindings and theme configuration; it does not include old framework code or dependencies. Core services and console protocols remain unchanged.

Dashboard: dark ribbon with Start, PS4 / Modding, Games, Tools and View pages; PlayStation blue accent; PS4 illustration and heading; four summary cards; console connection, supported games and mod/tool entry panels. Counts reflect existing catalog/session data, without invented remote metrics. Keep current tool singleton behavior, connection ownership, leave warnings and notification behavior. Light theme remains available globally. Use Segoe UI and measured DPI-aware layouts.

Replace grid sources with bindings to native GridView and explicit row identity, preserving selection, multi-select, activation, rename and drag/drop. Replace navigation with native TreeList and gallery with native DevExpress GalleryControl. Keep confirmation defaults and no-activation notifications. Remove old project references, namespaces and runtime files from packaging; include and verify the required DevExpress runtime dependencies. Do not embed developer license files or machine settings.

Implementation sequence: snapshot sources; add DevExpress references and shared bindings; migrate all apps and UI tests; build; update dashboard; exercise offline windows, keyboard/dialog behavior, themes and packaging; run complete existing tests and produce a checked ZIP. No PS4 access is needed for this migration.
