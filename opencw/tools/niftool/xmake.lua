-- niftool - OpenCommonwealth's NIF reader/author CLI. Vendored into this project so the asset
-- pipeline owns its whole toolchain (it was previously built out of the now-retired
-- Tools/PluginEditTool).
--
-- Build on Windows (PowerShell, NOT Git Bash -- Git Bash picks MinGW and finds no MSVC):
--   xmake f -p windows -a x64 -m release -y
--   xmake -y
-- Output: build/windows/x64/release/niftool.exe
--
-- Build on Linux:
--   xmake f -p linux -m release -y && xmake -y     -> build/linux/x86_64/release/niftool
--   ./build-windows.sh                             -> build/mingw/x86_64/release/niftool.exe

set_project("niftool")
set_languages("c++17")
add_rules("mode.debug", "mode.release")
if is_mode("release") then
    set_optimize("fast")
end

-- xmake-repo's `half` declares no mingw support, which is the only thing stopping a Linux host
-- from cross-building niftool.exe. It is header-only, so the local override just adds the platform.
add_repositories("niftool-repo xmake-repo")

add_requires("nlohmann_json")
add_requires("half", "miniball")            -- nifly's deps

includes("extern/nifly")

target("niftool", function()
    set_kind("binary")
    set_license("GPL-3.0")                   -- nifly embedding
    add_files("src/**.cpp")
    add_includedirs("src")
    add_packages("nlohmann_json")
    add_deps("nifly")                        -- headers are include-prefix-free: #include "NifFile.hpp"
    add_packages("half", "miniball")
    if is_plat("windows") then
        add_cxflags("/utf-8", "/bigobj")
    end
end)
