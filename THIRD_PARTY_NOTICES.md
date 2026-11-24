# Attribution and dependency notices

The integrations invoke independently developed upstream programs as separate processes. Their source and binaries are not included in our release ZIP.

| Upstream | Recorded license | License/source |
|---|---|---|
| VIctory | GPL-3.0-or-later | https://github.com/Marvisak/VIctory |
| XCL | GPL-2.0-only | https://github.com/ivall/xcl |
| Leonida fork | GPL-3.0-or-later | https://github.com/tudorek12345/Leonida |
| Xbox2Windows | MIT | https://github.com/R0Y00/Xbox2Windows |

Preserve their license and third-party files in downloaded checkouts. Engine/tool bundles generated locally include files required by the adapters; redistributing those bundles is outside this launcher release process and requires reviewing their licenses separately.

The self-contained Windows launchers include Microsoft .NET and Windows Desktop runtime components distributed by the .NET SDK. Their licenses and notices are bundled under `runtime-notices` in each launcher folder. Source and notices: https://github.com/dotnet/runtime and https://github.com/dotnet/windowsdesktop. No system-wide .NET runtime installation is required.

Other optional downloads include Rust, LLVM, SPIR-V Tools, micromamba, SDL3 and Vulkan headers. Their licenses remain applicable. The dependency managers and upstream source trees retain their notices. See `docs/DOWNLOADS.md` for origins.
