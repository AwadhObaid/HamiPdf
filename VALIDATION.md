# 0.11.0 validation

- Release WPF build succeeded; existing EditorWindow.TextInput warning only.
- UpdateChecks passed: semantic numeric comparison incl 4-part installed versions; equal/older releases ignored; drafts/prereleases skipped; missing installers and foreign/HTTP URLs rejected; Arabic notes retained.
- ScanChecks rerun passed. No scanner code changes in this phase.
- Windows self-contained win-x64 publish succeeded.
- Windows interactive updater acceptance, Inno Setup, GitHub CLI authentication/upload/create/publication remain pending on the user's machine.
- User confirmed scanner 0.10.0 successful before this phase.

Historical validation:

# 0.10.0 validation

Completed in Linux preparation environment:
- WPF Debug build and self-contained win-x64 Release publish succeeded with .NET 8.0.408 and EnableWindowsTargeting=true.
- The two reported CS0108 warnings are the pre-existing EditorWindow.TextInput name hiding, emitted by WPF temporary/final builds; zero errors.
- ScanChecks executed successfully: image embedding, page order, dimensions, 90/270-degree rotation, single-page export, existing-file protection, missing-image failure atomicity, temp cleanup, empty and invalid dimensions rejected.
- Exported two-page synthetic PDF rendered with Poppler; first rotated page visually checked.
- GeometryChecks, PageChecks and FormPageChecks executed successfully.
- OverlayChecks and FormChecks compiled successfully. Runtime requires Windows/WPF.
- LaunchChecks compiled but could not run here: sandbox denied the named-pipe socket (SocketException 13). The unchanged Windows build-run gate still executes this check; no checks were bypassed in delivery.
- Self-contained publish includes NAPS2.Worker.exe (x86), managed SDK packages, and both x86/x64 TWAIN DSM libraries. SDK native library search paths reviewed.
- Scanner UI verification is added to the published executable's --verify-ui gate; it runs on Windows in build-installer.ps1.

Pending on user's Windows:
- WPF visual/interaction checks; real WIA, TWAIN x86/x64/legacy and eSCL devices.
- Feeder, duplex, native UI, cancellation, unplug/paper-out/driver-failure behavior.
- Overlay/Form/Launch runtime checks and Inno Setup installation/upgrade.
- No actual scanner was connected in the preparation environment. No claim of universal hardware compatibility or completed Windows acceptance is made.

Historical release validation follows:

# 0.9.2
Application compiled with Windows targeting. New published-UI runtime gate and installed DLL hash verification require Windows and are executed by build-installer.ps1/install-machine.ps1 respectively. Earlier results below are historical.

# 0.8.2 verification
WPF project build succeeded with EnableWindowsTargeting=true using locally cached packages. The original PNG is 1254x1254; ICO has 16/24/32/48/64/128/256 frames. Toolbar now explicitly references the PNG resource and high-quality scaling. No artwork edits were made. About window and metadata compile successfully. Windows runtime visual inspection at the user's display scaling is pending. Prior validation below concerns earlier releases.

Installer correction: replaced the rejected function-local const with a String variable assigned after begin. Migration now requires a matching SHA256 marker before uninstallation. Reviewed and archive-checked; Inno compilation must run on Windows.

# Packaging update 0.8.1
Installer and migration script reviewed for Program Files 64-bit, admin-only setup, common shortcuts, HKLM registration, old per-user detection, explicit uninstaller invocation, and unchanged PDF UserChoice. The archive was verified. Windows Setup/uninstall/migration execution remains to be tested on Windows. Application code is unchanged except version metadata.

Prior 0.8.0 validation follows:

# Validation — HamiPdf 0.8.0

Performed in the preparation environment:
- Complete WPF project cross-compiled with .NET 8.0.408 and EnableWindowsTargeting=true: succeeded, zero errors. Existing EditorWindow.TextInput name-hiding warning remains.
- New Windows FormChecks project compiled: succeeded, zero errors/warnings.
- Geometry checks executed: passed (20 page/matrix checks plus stroke checks).
- Page checks executed: passed (form guard, ordering/merge/extraction, repeated save, overwrite protection).
- Browser form window tested in Chromium against 55.pdf: 70 inputs; Arabic text; checkbox checked then unchecked; save cancel recovery; second save; reopening; all passed.
- Independent pypdf readback: 70 canonical fields and 70 page widgets retained; Arabic value and one-page count retained.
- Browser form layout visually inspected at 1120x800.

Not executed here:
- Windows WPF runtime: FormAppearance raster rendering and its FormChecks runtime assertions.
- Native WebView2 window/dialog integration, Windows printing, and Inno Setup build.

The build-run.ps1 gate runs FormChecks on Windows before launching. Follow PHASE08_README_AR.md to check 55.pdf after saving, including appearance before field focus and print preview. Browser tests do not by themselves prove WPF appearance generation.
