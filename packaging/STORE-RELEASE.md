# Resonant Tuner: Microsoft Store Release

Status: Store identity supplied by the owner and recorded in the manifest. The
release script has generated a portable x64 ZIP and unsigned MSIX; archive contents
and checksums have been verified. Installation, certification, upload, and
publication have not been performed by this work.

## Recommended Route

Use an MSIX submission for this native WinUI desktop app. Keep the existing
unpackaged F5 development workflow. Start with Windows 11 x64; add ARM64 only
after hardware validation. No Azure hosting is needed.

Microsoft currently provides signing and hosting for Store MSIX packages. You do
not need to purchase a CA-trusted signing certificate for that route. Local
sideload testing has separate signing/trust requirements; do not install a test
certificate into the machine trust store without deliberate approval. EXE/MSI
submissions have different signing and hosting requirements.

Source: [MSIX package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements).

## First Steps Together

| Step | Owner | Action |
| --- | --- | --- |
| Developer account | You | Open https://storedeveloper.microsoft.com and complete account/identity verification yourself. Use an existing developer account if you have one. |
| Account type | You | Choose Individual only for a personal/non-business project; Company for business, trade, or professional distribution. Do not use your employer's identity or tenant unless authorized. |
| Product name | You | Store product identity supplied; confirm the reserved customer-facing name in Partner Center. |
| Package identity | Copilot | Supplied identity recorded below and in the manifest; verify it against Partner Center during package validation. |
| Release choices | You | Approve the publisher/brand, free or paid pricing, markets, support contact, privacy-policy location, code/artwork rights, and final release. |
| Package preparation | Copilot | Implemented in Build-Release.ps1; rebuild final binaries and revalidate package contents after source changes. |
| Certification checks | Together | Validate the packaged application on a clean Windows installation and review the actual package and Store listing before submission. |
| Submission/publication | You | Complete Partner Center's first submission and explicitly approve when the app becomes public. |

The current new-account onboarding flow documents no registration fee for either
account type. It involves personal or business verification, which must stay
between you and Microsoft. Individual-to-Company conversion is not supported;
choose the ownership model before registering.

Sources: [account onboarding](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account),
[name reservation](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/reserve-your-apps-name),
[Store policy 10.14](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies#1014-account-type).

## Store Identity

Owner-supplied values, not independently verified against Partner Center:

| Identifier | Value |
| --- | --- |
| Package/Identity/Name | `14420Scriptwizard.ResonantTuner` |
| Package/Identity/Publisher | `CN=0BC2802E-0560-42A0-95F2-F0AEEE4F57FA` |
| Package/Properties/PublisherDisplayName | `Scriptwizard` |
| Package family name (PFN) | `14420Scriptwizard.ResonantTuner_mcvcvj63bcy3p` |
| Package SID | `S-1-15-2-3080310220-4034073290-2139406402-2369367518-4212779763-1166220594-4276061116` |
| Store ID | `9N2FKLKSRGHG` |

The supplied label `PublishedDisplayName` is mapped to the manifest element
`PublisherDisplayName`; the application's display name is `Resonant Tuner`.
PFN, Package SID, and Store ID are reference identifiers, not additional manifest
identity attributes. These values are identifiers, not credentials.

## Packaging Status

- [Build-Release.ps1](Build-Release.ps1) publishes self-contained Release binaries,
  generates logo assets, and builds the ZIP, unsigned MSIX, and checksums. Run it
  from the repository root; optional `-Version 0.1.0.0` controls the package version.
- [Resonant.App.csproj](../src/Resonant.App/Resonant.App.csproj) deliberately sets
  `EnableMsixTooling=false` and `WindowsPackageType=None`; its Debug build is not an uploadable MSIX.
- The manifest targets Windows Desktop on Windows 11 x64, launches `Resonant.exe`
  as a full-trust desktop app, and declares microphone access.
- Existing [Resonant.png](../src/Resonant.App/Assets/Resonant.png) and
  [Resonant.ico](../src/Resonant.App/Assets/Resonant.ico) can be inputs after artwork
  approval. The release script generates the four PNG assets in its staging folder.
- The manifest declares all five shipped languages, and the release script checks
  their managed satellite assemblies. Do not assume `x-generate` discovers .NET RESX resources. Verify
  language selection in the installed package, not just the unpackaged executable.
- [README.md](../README.md) records pending MIT approval. Confirm ownership and
  licensing before any public distribution; retain [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES).
- No Store package installation, update, uninstall, certification, or ARM64
  hardware result has been established by the localization work.

## Listing And Privacy

Provide a public privacy-policy URL even though Resonant Tuner is offline: Store policy
10.5.1 explicitly requires privacy policies for Win32 products. Include a support
contact, accurate system/microphone requirements, genuine screenshots, and the
IARC age-rating questionnaire. Do not guess the rating or promise certification.

The app now includes English, Dutch, German, French, and Spanish translations.
Have fluent speakers review them before listing those languages as release-ready.
Store descriptions must also be localized for declared languages; translating the
app does not automatically translate its Store listing. Note that settings and
custom tunings last only for the current session. Avoid unmeasured accuracy or
latency claims, and do not claim validated ARM64 support yet.

Privacy text to review and publish under your own identity:

> Resonant Tuner uses the selected microphone to estimate musical pitch and display a
> live spectrum. Listening begins when the tuner opens and can be paused. Audio
> is processed in memory on your device and is not recorded to files, uploaded,
> or shared by Resonant Tuner. Reference playback temporarily pauses microphone
> analysis. Closing Resonant Tuner releases the microphone and discards session
> settings and custom tunings. Resonant Tuner contains no advertising, analytics,
> account system, or cloud service. You can revoke microphone access in Windows
> Settings. Add the publisher's privacy/support contact and effective date before
> publication. Microsoft's Store and Windows platform data practices are separate
> from the app's processing described here.

This draft needs owner review and a real hosted URL; it is not a legal opinion or
an already-published policy.

Source: [Store policies 10.1, 10.5, 10.7, 11.2, and 11.11](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies).

## Release Gate

- [ ] Approve ownership, name, pricing, markets, licensing, artwork, and translations.
- [x] Record the owner-supplied Store identity in the manifest and release notes.
- [x] Generate the release MSIX and logo assets; verify archive payloads and SHA-256 checksums.
- [ ] Verify identity against Partner Center.
- [ ] Verify installation on Windows 11 without development tools or preinstalled app runtimes.
- [ ] Verify microphone consent/denial/retry, Pause/Resume, device changes, reference audio, and cleanup.
- [ ] Verify packaged language fallback, decimal formatting, all pages, compact mode, keyboard and Narrator.
- [ ] Complete real-instrument, sustained-use, latency, and supported-hardware checks.
- [x] Correct the low-B/A4=415 Hz range boundary and add synthetic regression coverage.
- [ ] Review current Partner Center package validation and certification requirements; retain evidence.
- [ ] Publish approved privacy/support pages; finish localized listings, screenshots, ratings, and certification notes.
- [ ] Review all Partner Center validation results and the first submission together.
- [ ] Obtain explicit owner approval before submission and public release.

For later automation, Microsoft's Store Developer CLI exists, but its current
MSIX documentation requires the first publication through Partner Center. It
requires associated Entra/API access and currently documents update support for
free products only. Do not create tenant associations or automate publication
without approval. Automated builds can remain separate from the public-release gate.

Source: [Microsoft Store Developer CLI](https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/overview).