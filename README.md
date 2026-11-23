# Movo

**Movo** is a small, standalone programming language for Windows. Programs are plain text files with the `.mv` extension; libraries use `.mvl`. The interpreter is a single `movo.exe` written in C# — no Python, no C++, nothing to install beyond what Windows already includes.

```mv
Start in {
 printon!("lol")
}
```

## Features

- Python-like syntax (indentation or `{ }` blocks), functions with `def` / `fn`, classes, `try`, comprehensions
- Built-in GUI toolkit (windows, buttons, text boxes, keyboard handling)
- Built-in local web server for small sites (`.mvs` host files)
- Native libraries `web` (HTTP with cookie sessions) and `browser` (drive Chrome/Edge)
- Libraries written in Movo itself (`.mvl`) and downloadable with `movo install NAME`
- Double-click `.mv` files to run them (custom file icons)

> Libraries are loaded with `put.here NAME` — the word `import` is intentionally not used in Movo.

## Requirements

- Windows 7 / 8 / 10 / 11 (the C# 4 compiler `csc.exe` from .NET Framework 4 is already part of Windows)

## Quick start

### Option A — one-click installer

Download `dist/Movo.bat` and double-click it. It unpacks the sources, builds `movo.exe`, registers the file types and adds Movo to your `PATH`.

### Option B — from the repository

```bat
git clone <your-repo-url> movo
cd movo
install.bat
```

Or just build without installing anything:

```bat
build.bat
build\movo.exe examples\hello.mv
```

Open a **new** command prompt after installing, then:

```bat
movo examples\hello.mv
movo --help
```

## Command line

```
movo file.mv                 run a program
movo install <library>       download a library into the library folder
movo --init                  create the library folder
```

The library folder is `C:\movo\library` by default; set the `MOVO_LIB` environment variable to change it. `movo install` reads the library list from `MOVO_INDEX_URL` (a default index is built in).

## Repository layout

```
src/        interpreter: parser, interpreter, built-ins, GUI, local web host, `movo install`
libs/       native libraries (web, browser) built as .dll
assets/     icons for .mv and .mvl files
examples/   sample programs
dist/       Movo.bat — original single-file installer (sources embedded as base64)
build.bat   build from source into ./build
install.bat build from source and install for the current user
```

## Optional add-ons

`install.bat` and `dist/Movo.bat` also install the add-ons **Https**, **Host** and the `.mvs` file type if a folder named `movo-addons` (containing `library/Https.mvl`, `library/Host.mvl` and `mvs_run.mv`) sits next to the installer. That folder is not part of this repository snapshot.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)
