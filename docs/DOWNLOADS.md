# Downloads and build execution

There is no telemetry or automatic upload in the launcher integrations. User-triggered setup, clone, build and update actions may contact:

| Action | Origin |
|---|---|
| Clone/check/update upstream source | The four exact GitHub repositories in `upstreams.json` |
| Rust toolchains and packages | Rust/rustup distribution servers and crates.io, including dependencies selected by upstream lockfiles |
| Native CMake dependencies | URLs and hashes declared by the selected upstream, including Khronos Vulkan headers and SDL |
| Python build dependencies | PyPI packages pinned by the selected upstream requirements |
| Leonida shader tools | Official micromamba endpoint `https://micro.mamba.pm/api/micromamba/win-64/latest`, then conda-forge LLVM and SPIR-V Tools |
| Launcher build | .NET SDK/NuGet framework targeting and apphost packages |

The four upstream URLs and tested commits are reviewed release inputs. Dependency downloads are not restricted to GitHub. Upstream build scripts execute locally after a user requests a build; a source commit or hash is an identity check, not a guarantee of safety. Installed engine/tool revisions are shown in the UI.

System prerequisites are detected, not installed silently. Use official installers linked by the prerequisite check for Git, Python, Rust, Visual Studio C++ tools and CMake/Ninja. Do not distribute local compiler installations or package caches with this repository.
