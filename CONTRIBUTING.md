# Contributing to Movo

Thanks for helping! Movo is a small interpreter written in plain C# (.NET Framework 4, no NuGet packages), so it builds with the compiler that already ships with Windows.

## Build

```bat
build.bat
build\movo.exe examples\hello.mv
```

## Layout

| Path | What it is |
|------|------------|
| `src/Values.cs`, `Ast.cs`, `Parser.cs`, `Interp.cs`, `Builtins.cs` | the language core: values, syntax tree, parser, interpreter, built-in functions |
| `src/Program.cs` | command-line entry point (`movo file.mv`, `movo install`, `--init`) |
| `src/Gui1.cs`, `Gui2.cs`, `Gui3.cs` | the GUI toolkit available to `.mv` programs |
| `src/Host.cs` | local web server (`host.local(...)`) |
| `src/MovoInstall.cs` | `movo install <library>` downloader |
| `libs/Web.cs`, `libs/Browser.cs` | native libraries `web` and `browser` (compiled to `.dll`) |
| `assets/` | icons for `.mv` and `.mvl` files |
| `dist/Movo.bat` | the original single-file installer (sources embedded as base64) |

## Pull requests

- Keep changes focused; one feature or fix per PR.
- Add or update a small example in `examples/` when you add language features.
- Note: in Movo you load libraries with `put.here NAME`, not `import`.
