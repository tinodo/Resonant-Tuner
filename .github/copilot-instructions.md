# Copilot instructions for Resonant Tuner

Resonant Tuner is an offline guitar/instrument tuner for Windows: WinUI 3, C#, .NET 10, Windows App SDK. Audio never leaves the device.

## Architecture

- `src/Resonant.Core` has no WinUI/WinRT dependency: pitch math, YIN estimator, tracker, spectrum, tuning session, `TunerController`, localization (`Text.Get`/`Text.Format`, RESX catalogs).
- `src/Resonant.App` is the WinUI 3 app: `AppServices` composition root, `AudioGraph` capture/tone adapters, view models, XAML pages.
- Tests: `tests/Resonant.Core.Tests` (synthetic DSP/session/localization) and `tests/Resonant.Audio.Tests` (controller with handwritten fakes). MSTest.

## Review priorities

- **Privacy:** flag any network, telemetry, account, disk persistence or audio recording. The app is offline and session-only by design.
- **Audio thread safety:** capture/render callbacks must not allocate, block or touch UI objects. Check disposal and device-change/lifecycle ordering in `TunerController` and the `AudioGraph*` classes.
- **Localization:** every user-visible string goes through `Text.Get`/`Text.Format` and exists in all five catalogs (`Strings*.resx`: neutral English, nl, de, fr, es) with identical `{n}` placeholders. Keys are case-insensitively unique. XAML uses `x:Bind loc:Text.Get('...')`.
- **Live language switch:** view-model text must refresh on `AppCulture.Changed`; never replace a ComboBox `ItemsSource` bound to `SelectedItem` (selection is lost); pages are cached, so they refresh on `Loaded` if they missed a change.
- **XAML/WinUI:** `x:Bind` correctness, accessibility names, keyboard operation, high-contrast and reduced-motion behavior. No `x:Bind` in `StateTrigger` inside item templates.
- **DSP changes** need a synthetic-signal test; do not claim real-instrument accuracy from synthetic results.
- **Supply chain:** dependencies are pinned with `packages.lock.json` and restored with `--locked-mode`; flag unpinned or unmaintained packages, and any signing material (`*.pfx`, `*.snk`) or secrets.
- **Package publishing:** `publish-package` in `release.yml` must stay gated on the tested `release` job (`needs`), skip drafts, and push the exact package artifact that job validated. Flag anything that widens `packages` or `contents` write permissions, adds a PAT or secret, adds checkout or repository script execution to that job, or lets a package publish from a draft or a commit outside `main`.

## Conventions

- C# with nullable enabled; keep comments to one short line and only where the code cannot say it.
- Add or update tests with behavior changes; CI runs both test projects on Windows.
