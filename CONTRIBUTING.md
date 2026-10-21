# Contributing to WebHut

Thanks for helping. WebHut aims to stay small, dependable and easy to understand, so please read this before opening a large pull request.

## Ground rules

- **Local development only.** Features that make WebHut act like a production server are out of scope.
- **Official binaries, unmodified.** WebHut downloads and configures upstream builds; it doesn't patch or rebuild them.
- **No new dependencies.** The code uses only what ships with .NET Framework 4.8 and compiles as C# 5, so anyone on Windows can build it with the built-in `csc.exe`. No NuGet packages.
- **Never overwrite user files.** Config, `php.ini`, custom vhosts, databases and sites must survive every operation and upgrade.
- **Everything the GUI does should be possible from `webhut`** (or by editing a documented file).

## Getting started

```bash
# Linux/macOS with Mono
./build/build.sh
./tests/run.sh
./tests/integration.sh   # needs: apt install mariadb-server mariadb-client
./tests/apache-smoke.sh  # needs: apt install apache2 libapache2-mod-php
```

```powershell
# Windows
powershell -ExecutionPolicy Bypass -File build\build.ps1
```

See [docs/06](docs/06-control-panel.md#inside-the-code) for a map of the code and [docs/07](docs/07-packaging-and-releases.md) for the release process.

## Pull requests

1. Open an issue first for anything bigger than a bug fix, so we can agree on the approach.
2. Keep changes focused; one topic per PR.
3. Add or update tests in `tests/Tests.cs` for logic changes, especially the download-page parsers.
4. Update the docs in the same PR if behavior changes.
5. Test on Windows if you touch process handling, the UI, or anything in `src/Core/Services.cs`, and say in the PR what you tested.

## When a publisher changes its download page

This is the most likely breakage. To fix it:

1. Save the new page or JSON as a test sample in `tests/Tests.cs`.
2. Update the matching `Parse…` method in `src/Core/Resolver.cs`.
3. Until a release ships, maintainers can pin a URL in `versions.json` so existing installs keep working.

## Reporting bugs

Include `webhut status`, the last 50 lines of `logs\webhut.log`, the relevant server log, your Windows version, and the steps to reproduce. Remove passwords first.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
