# v0.1.0-alpha.3 — self-contained experimental preview

Portable Windows x64 launcher interfaces for VIctory, XCL, the specified Leonida fork, and Xbox2Windows research tools.

- First-run game/data-folder selection; no preconfigured personal paths.
- Explicit source cloning at recorded revisions, prerequisite checks, local builds, manual updates and rollback.
- Leonida shader-tool setup, shader generation, Pause/Resume, and confirmed managed-cache deletion.
- Leonida integrated VIctory / native D3D12 selection, source build and separate version/rollback metadata. Integrated shaders compile during use into a separate cache; native Vulkan preparation remains available.
- Offline Xbox2Windows reports and source-built GPU checks.
- Allowlisted release packaging and SHA-256 checksum.

**Extract the ZIP and double-click the appropriate `Launch-*.cmd`. No .NET installation or terminal commands are needed to open the launchers.** Keep the extracted folders together. This release includes local .NET 10 Windows Desktop runtimes. Upstream engine builds still require developer tools and may use significant disk space and time; clone/build/update actions are available through the UI. Download origins are documented in `docs/DOWNLOADS.md`.

Experimental purposes only. No game files, extracted shaders, upstream engine binaries or compiler installations are included. This release does not promise game startup or gameplay. The launcher is unofficial; each upstream project is independently maintained and licensed.

Known local result: Leonida runtime revision `5dd9c2c21093` started and then exited with breakpoint exception `0x80000003`, after reporting an unserved `xgameruntime.dll!XErrorReport` lookup. Shader generation had completed successfully. The last diagnostic does not establish the exact cause of the exception. Shader preparation does not imply that this runtime can boot a game.

Local verification covers launcher builds, isolated first-run setup, synthetic launcher/process tests, packaging exclusions, and script parsing. This public package has not been independently tested on a fresh Windows installation or used for a full-game run. Upstream CI and GPU checks are separate from launcher CI.

Integrated VIctory revision `6f5b35fd81a2` built locally and inspected the supplied EXE without executing it. This does not verify game startup or resolve the older native backend's crashes.
