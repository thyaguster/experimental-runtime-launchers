# Experimental Runtime Launchers

## Install on Windows x64

**[Download the launcher ZIP (about 191 MB)](https://github.com/thyaguster/experimental-runtime-launchers/releases/download/v0.1.0-alpha.3/experimental-runtime-launchers-0.1.0-alpha.3-win-x64.zip)** · **[Open the release page](https://github.com/thyaguster/experimental-runtime-launchers/releases/tag/v0.1.0-alpha.3)** · [All releases](https://github.com/thyaguster/experimental-runtime-launchers/releases)

1. Download the **launcher ZIP** using the link above. Do not choose GitHub's **Source code** ZIP. Expand **Assets** on the release page if the downloads are collapsed.
2. Right-click the downloaded ZIP and choose **Extract All**. Extract it to a writable folder on a drive with enough free space. Keep all extracted folders together.
3. Open the extracted folder and double-click **`Launch-Leonida.cmd`**, **`Launch-VIctory.cmd`**, **`Launch-XCL.cmd`** or **`Launch-Xbox2Windows.cmd`**. No terminal commands or .NET installation are needed to open a launcher.
4. In first-time setup, select your game EXE if available and choose a **new, empty project data folder** for that project's source, builds and caches. You can put it on another drive. Use a different data folder for each project.
5. Click **Prerequisites**. If tools are missing, install them using the official links shown, then close and reopen the launcher. Engine builds need developer tools even though the launcher itself is ready to run.
6. Click **Clone upstream**, then **Build engine** (or **Build tools** for Xbox2Windows). Wait for the task to finish successfully. These buttons download source and build it locally.
7. For Leonida, leave **Integrated VIctory / native D3D12** selected and click **Launch integrated VIctory** after building. For standalone VIctory or XCL, use **Launch**. Xbox2Windows currently provides research reports and GPU checks rather than a game-launch button.

**Leonida's older Native gta6rt / Vulkan option:** install its shader tools and click **Generate shaders first** before launching. Integrated VIctory compiles shaders during use and does not need that separate Vulkan preparation step.

This is an **experimental pre-release**, not a game download or a promise of successful game startup. No game files or upstream engine binaries are included. While this repository is private, you must sign in to a GitHub account with repository access to see or download the release.

---

**Experimental Windows launchers and build managers for independent compatibility and recompilation research projects.**

This repository provides setup, build, update, shader-preparation and diagnostic interfaces. It downloads the selected project's source from the upstream GitHub repository and builds supported components on the user's PC. Build dependencies may also come from official vendor endpoints, PyPI, Rust distribution servers and conda-forge.

Releases include our launcher code and integration scripts. They contain **no game files, extracted shaders, console SDKs, upstream engine binaries, preinstalled compiler tools or personal settings**. Users supply their own required inputs. This project does not implement the underlying engines or promise successful startup or playable gameplay. It is unofficial and independently maintained.

## Supported integrations

| Project | Upstream | Scope in this launcher |
|---|---|---|
| VIctory | [Marvisak/VIctory](https://github.com/Marvisak/VIctory) | Experimental runtime, diagnostics/WARP selection, builds and updates |
| XCL | [ivall/xcl](https://github.com/ivall/xcl) | Experimental runtime, shader cache location, builds and updates |
| Leonida | [tudorek12345/Leonida](https://github.com/tudorek12345/Leonida) | Integrated VIctory / D3D12 and native Vulkan backends, separate builds/updates/rollback, Vulkan shader preparation and Pause/Resume |
| Xbox2Windows | [R0Y00/Xbox2Windows](https://github.com/R0Y00/Xbox2Windows) | Offline import reports and source-built GPU checks; no general game-launch button |

Upstream projects change independently. Read their current documentation before running an experiment. Tested source revisions are recorded in [`upstreams.json`](upstreams.json). **Clone upstream** starts at the recorded revision; **Update** explicitly fetches and builds newer upstream code. A previous successful installed build is retained for rollback.

## First use

1. Download the **win-x64 ZIP** from Releases. The launchers include their own .NET runtime; no .NET installation or terminal commands are needed to open them.
2. Extract the whole release ZIP into a writable folder on a drive with space. Keep its `tools` folder and project subfolders together; do not run inside the ZIP or move an EXE out of its `LauncherApp` folder.
3. Open the appropriate `Launch-*.cmd`. First-time setup asks for an optional game EXE and a **new, empty data folder** on a drive with enough space. No personal drive paths are preconfigured.
4. Select **Prerequisites** first. Install the missing tools from the official links shown and reopen the launcher. Then select **Clone upstream** and build the selected engine/tool suite.
5. Leonida defaults to **Integrated VIctory / native D3D12**, built from this fork's `runtimes/victory` source with diagnostics and the Series S profile. Build the engine, then try Launch; shaders compile during use into `launcher-data/integrated-victory/cache`. WARP and the Series S menu presentation workaround are optional diagnostic controls. Build/update/rollback act on the selected backend. For **Native gta6rt / Vulkan**, install shader tools and **Generate shaders first** before Launch. Xbox2Windows offers research checks rather than game execution.

Required native build tools include Git for Windows, Python 3.12+, Visual Studio C++ Build Tools with Windows SDK 10.0.26100+, and, depending on the adapter, Rust/rustup or CMake/Ninja. Prerequisite checks do not silently install system-wide software. Explicit build/setup actions can download dependencies and execute build scripts.

## Storage and controls

Source checkouts, engine versions, caches, logs, saves, tool environments and shader work stay in the chosen project data folder. Directory junctions preserve stable paths from the extracted launcher folder. **Apply folder** migrates a project to an empty destination and verifies copies before removing the old data copy. Do not manually delete the folder links or move the installation while tasks are active.

Leonida Pause suspends its preparation worker tree in memory. Resume continues the same run; Stop ends it and retains reusable stages. Cache deletion requires confirmation, is blocked during active or paused work, and removes only managed shader work. Other engines have different game-file write behavior: there is no universal read-only guarantee across all integrations.

No updates, builds or uploads happen automatically on startup. Logs and diagnostic reports stay local. Launcher updates are separate from upstream updates: install a new launcher release to change this integration code.

## Build and release

Install the .NET 10 SDK on x64 Windows, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-Public.ps1
```

This builds the four launcher interfaces, tests a clean extracted package with synthetic setup data, and produces an allowlisted portable ZIP plus a SHA-256 checksum. It never clones upstream engines, builds game inputs or includes the current working directory wholesale. Outputs default to `artifacts`; use `-OutputRoot` to choose another drive.

The GitHub Actions workflow runs the same package checks. A `v0.1.0-alpha.1`-style tag creates a **draft pre-release**, ready for asset and note review before publication. See [`docs/PUBLISHING.md`](docs/PUBLISHING.md).

## License, attribution and limits

Our launcher/integration source is GPL-3.0-or-later; see [`LICENSE`](LICENSE). Upstream projects and downloaded dependencies retain their own licenses and attribution. No upstream source or binaries are bundled. See [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) and [`docs/DOWNLOADS.md`](docs/DOWNLOADS.md).

This is an alpha research utility, without warranty. A successful build, shader conversion or GPU test is not evidence of complete game compatibility. No affiliation with upstream maintainers, game publishers, console vendors or Microsoft is implied.
