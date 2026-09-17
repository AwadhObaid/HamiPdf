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
