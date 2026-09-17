# File Splitter

A small Windows app that splits any file into parts no bigger than a size you choose, and joins the parts back into the original file.

## Run

Download `FileSplitter.exe` from the latest GitHub release or Actions run (built for Windows x64), or build it yourself (see below). Double-click it on Windows 10/11; you don't need to install .NET. There's also an ARM64 build for Windows on ARM.

### Split
1. **Split** tab: pick or drag in a file.
2. Set the **max part size** (KB / MB / GB) and the output folder.
3. Click **Split**. This creates `name.ext.001`, `name.ext.002`, …

### Join
1. **Join** tab: pick or drag in **any** part (for example `name.ext.001`). The app finds the other parts on its own.
2. Click **Join**. This rebuilds `name.ext`.

## Command line

```
FileSplitter split "C:\videos\movie.mp4" 700MB [outDir]
FileSplitter join  "C:\videos\movie.mp4.001" [outFile]
```

## Join without the app

The parts are plain byte chunks, so Windows can join them on its own:

```
copy /b movie.mp4.001 + movie.mp4.002 + movie.mp4.003 movie.mp4
```

## Build (from macOS, Linux, or Windows with the .NET 8 SDK)

```bash
dotnet publish src/FileSplitter -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish/x64
```

For Windows on ARM, use `-r win-arm64 -o publish/arm64`.

## Tests (run on Windows)

```
powershell -ExecutionPolicy Bypass -File tests\test-cli.ps1 -Exe publish\x64\FileSplitter.exe
powershell -ExecutionPolicy Bypass -File tests\test-gui.ps1 -Exe publish\x64\FileSplitter.exe
```

`test-cli.ps1` checks split/join round trips. `test-gui.ps1` uses the window through UI Automation (split, join, cancel), so it needs a desktop session.

## CI

`.github/workflows/windows.yml` builds the x64 exe on `windows-latest` and runs both test scripts on every push. When you push a `v*` tag, it also publishes `FileSplitter-win-x64.zip` as a GitHub release.
