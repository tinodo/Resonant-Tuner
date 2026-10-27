# Resonant Tuner

An offline, accurate instrument tuner for Windows (WinUI 3, C#, .NET 10). Audio is processed in memory on your device. There is no recording, upload, telemetry, account, or network feature.

Local Windows x64 builds, synthetic DSP/controller tests, and microphone/reference-tone operation have been verified. Real-instrument accuracy, sustained latency, and ARM64 hardware validation remain open.

## Listening and devices

Listening starts automatically when the tuner first opens. Pause/Resume controls listening for the current session; navigating between tabs does not undo a pause. Audio stays in memory and capture is released when Resonant Tuner closes.

Reference playback temporarily pauses capture to avoid measuring the app's own tone. Stopping the reference resumes listening only if it is still wanted; Pause during playback cancels that automatic resume. Selecting another string updates an active reference tone.

Input-device or channel changes in Sound & display restart active capture. During reference playback they apply when listening resumes; while paused they do not start capture. Output changes move an active reference tone to the new device without changing its note, or simply select the device for the next reference when playback is off. Device transitions can cause a brief interruption, and failures are reported rather than shown as successful listening.

## Live spectrum

The tuner background displays actual microphone frequency content as a filled cyan contour with a fading magenta peak-hold trace and logarithmic frequency grid. It uses a 4096-sample Hann-windowed FFT and 64 bands from 40 Hz to 12 kHz. Levels use a fixed -72 to 0 dBFS scale, not automatic gain. Analysis runs on the pitch worker; the native background refreshes at approximately 30 Hz with attack/release smoothing. It fades out when capture stops or pauses for a reference tone. The Spectrum switch in tuning options hides it for the current session. High-contrast mode suppresses the background, and reduced-motion settings disable smoothing and peak hold.

## Pitch tracking

Pitch detection uses at least 4096 samples at 48 kHz and updates every 33 ms. The estimator selects the deepest minimum in its first threshold valley and fits neighboring lag samples to reduce noise-driven errors. Tracking requires three consistent, confident frames before showing a new pitch and five before accepting a large jump or changing strings. A five-frame median and cents-domain smoothing damp isolated spikes while following small tuning adjustments. Short dropouts hold the previous reading as stale, never as a fresh in-tune lock; prolonged silence clears it. Tracking resets when capture restarts or the pitch search range changes.

Clean quiet notes below the normal 0.0015 RMS gate may be analyzed down to 0.0005 RMS, but only when periodicity confidence is at least 98%. This improves soft high-E sensitivity without relaxing note-change confirmation or accepting quiet broadband noise.

## Languages

Resonant Tuner includes English, Dutch, German, French, and Spanish. It follows the
Windows UI language at startup, with regional fallback (for example, `nl-BE` to
Dutch) and English for unsupported languages. Translations include navigation,
settings, built-in tuning descriptions, errors, and accessibility labels. User
tuning names, note notation such as `F#3`, and device names are not translated.
Numeric readouts follow the current culture. The interface language can be changed live
in Sound & display (Follow Windows, English, Nederlands, Deutsch, Français, Español);
the choice lasts for the current session only.

For a per-launch override without changing Windows settings, start the executable
with `--language nl-NL`, `--language de-DE`, `--language fr-FR`, `--language es-ES`,
or `--language en-US`. The override applies to that process only. Standard Windows
chrome continues to follow Windows. Fluent-speaker review is still required
before treating the translations as Store release-ready.

## Microsoft Store

See [the release guide](packaging/STORE-RELEASE.md) for the account, name reservation,
package identity, MSIX preparation, privacy, and certification steps. No Store
submission or publication has been performed. The current Debug executable is
unpackaged, not an uploadable Store package.

## Layout

| Path | Purpose |
|------|---------|
| `src/Resonant.Core` | Pitch math, presets, YIN-style estimator, tuning session, tuner controller. No WinUI/WinRT dependency. |
| `src/Resonant.App` | WinUI 3 app: pages, view models, `AudioGraph` capture and reference-tone adapters. |
| `tests/Resonant.Core.Tests` | Deterministic math, DSP and session tests on synthetic signals. |
| `tests/Resonant.Audio.Tests` | Controller and device-lifecycle tests with handwritten fakes. |
| `packaging/Package.appxmanifest` | Owner-supplied Store identity, desktop capabilities, and five shipped languages. |
| `packaging/Build-Release.ps1` | Repeatable self-contained x64 ZIP, unsigned Store MSIX, and SHA-256 checksums. |

## Known gaps

- Release packaging generates the portable ZIP, logo assets, and unsigned Store MSIX. Store identity still needs verification against Partner Center, and installed-package behavior needs validation. Unsigned MSIX files are not direct sideload installers. The project declares `win-x64` and `win-arm64`, but only local x64 execution has been validated.
- Raw/unprocessed capture behavior still needs device-specific verification.
- Windows UI Automation tests (`tests/Resonant.Ui.Tests`) are not written yet.
- Hardware gates (latency, 30-minute soak, real-instrument accuracy, Narrator, ARM64) are scheduled manual checks.
- Settings, custom tunings and calibration are session-only by design.

## Licence

MIT. See `LICENSE` and `THIRD-PARTY-NOTICES`.
