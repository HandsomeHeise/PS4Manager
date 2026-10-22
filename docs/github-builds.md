# Private source repository and automated builds

The repository is private at https://github.com/HandsomeHeise/PS4Manager. Git contains application/server sources, tests, assets and dependency manifests. It excludes compiled output, user settings, credentials, signing keys, developer licenses and proprietary DevExpress assemblies. Windows ZIP files are Actions artifacts and private Releases, not Git commits.

`Source checks` runs the independent .NET cores and server checks on GitHub-hosted runners. `Desktop build and release` runs on a dedicated Windows x64 self-hosted runner labeled `ps4manager-devexpress`, using the owner's DevExpress 25.2 installation. It runs only pushes to the private repository and manual dispatch, never pull-request code.

Pushing `v*` tags first requires the reusable hosted source/core/server checks to succeed, then builds, tests, packages and creates a private Release. The complete desktop suite and an explicit x86 Lua/Scintilla Studio suite must pass. A failed or cancelled prerequisite cannot publish a release. Builds with DX1000/DX1001/DX1002 license diagnostics are explicitly marked prerelease/evaluation. Register an eligible DevExpress license before distributing outside the owner's private evaluation. No license key is put in Git or a release archive.

The runner must be online. Its registration credentials and diagnostic logs stay in the owner's local runner installation outside this repository. Removing that runner in repository Settings → Actions → Runners disables desktop builds without affecting hosted source checks.

Current local runner: `PS4Manager-DevExpress`. It starts hidden at user logon through `PS4Manager GitHub Runner.lnk` in the current user's Startup folder. Its installation is `%LocalAppData%\PS4Manager\ci-runner`; a per-user mutex prevents duplicate listeners. The runner must remain dedicated to this private repository.
