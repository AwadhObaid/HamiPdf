# 0.11.0 — GitHub update feed and release publishing

- Includes scanner 0.10.0, user-confirmed successful on Windows.
- Daily optional startup check and manual About dialog check.
- Strict stable version/official installer asset validation, release notes, later/ignore/download actions.
- Browser download initiated by user; no background installer execution.
- Source upload with pinned baseline and source integrity manifest; separate public installer repository bootstrap.
- Publish as draft, read back/check uploaded installer hashes, then publish as latest; never overwrite a published version.
- Connector write returned HTTP 403. Source/repository/release publication must run with the provided scripts on the user's Windows account.

# 0.10.0 — Scanner integration

- Arabic scan dialog: WIA, TWAIN x86, TWAIN x64, legacy TWAIN and eSCL discovery.
- Flatbed/feeder/duplex requests; color/gray/monochrome; 150/300/600 dpi; A4/A5/Letter/Legal; optional native driver UI.
- Disk-backed completed PNG pages, preview, reorder, quarter-turn rotation, delete, repeated scans and cancellation.
- Atomic new-file PDF export with page-count, rotation and dimensions readback. Existing files never overwritten.
- Save-and-append opens the existing form-preserving page manager; scanned PDF saved separately first.
- Per-user preferences; bounded diagnostic log; defer disposal until driver operation/export finishes.
- Pinned NAPS2 SDK 1.3.0 with x86 worker; separate native x64 context; bundled x86/x64 TWAIN DSM.
- Installer verifies scanner files and compiled scanner UI. Windows scanner/hardware acceptance pending.

## 0.9.2 — Validate published UI
- Invalidate generated Release WPF output before publishing.
- Verify actual compiled About button, developer text and high-resolution logo before building Setup.
- Verify source copy and installed assembly hashes.

## 0.8.2 — About and toolbar logo
- About window with live assembly version, description, Awadh Faghmah credit and copyright.
- Developer metadata in the executable and installer.
- Toolbar uses the original high-resolution PNG with high-quality scaling and pixel-aligned layout; Windows ICO unchanged.
- Includes the corrected Program Files installer.

## 0.8.1 installer correction
- InitializeSetup uses a local string variable instead of an unsupported local const declaration.
- Migration only accepts Setup builds with the successful-build SHA256 file and matching hash, before running any old uninstaller.

## 0.8.1 — Machine-wide installation
- 64-bit Program Files default; admin-required Setup; no reuse of per-user install directory.
- Common shortcuts and machine-wide project/Open With registration.
- Current-user migration helper invokes registered old uninstaller before elevated new Setup.
- Existing per-user HamiPdf executable association retargeted after successful installation.
- PDF UserChoice is not overwritten.

## 0.8.0 — Interactive form filling
- Compact local PDF.js form window, launched from the active PDF tab.
- Interactive Save As; source protection, sibling temporary commit, previous-copy backup.
- Arabic text widget appearances without flattening pages or discarding form fields.
- Field/value and widget-tree verification; unsaved-close prompt.
- Windows FormChecks and browser regression harness.
- Form page management remains guarded. XFA, digital signing and PDF JavaScript are not supported.

# 0.7.2 - Compact secondary windows and page preview handling

- Compact editor/page-manager chrome, property spacing, adjustable columns, smaller viewer gutters.
- Updated editor fit calculation for reduced padding.
- Keep page selection enabled while preview renders; serialize rendering, discard stale results and defer disposal until rendering completes.
- Best-effort local exception logs for page import/export/preview failures.
- Original reported page-management failure remains unidentified without error details.
- XML/events/resources checked here; Windows build and visual/runtime verification required.

# 0.7.1 - Compact viewer interface

- Replaced oversized banner and duplicated filename with one compact toolbar.
- Styled document tabs and close buttons; filename uses LTR presentation for mixed-extension readability.
- Removed outer viewer gutters and reduced status bar padding.
- Kept WebView2 rendering/navigation and all editor/export/IPC behavior intact.
- Static XML, resource and event validation completed; Windows visual/build verification pending.

# 0.7.0 - Phase 07

- Persistent per-document WebView2 instances and scrollable tab headers, deduplication and disposal on close.
- Multi-select file open and drag/drop on WPF chrome; print/edit/page actions target the active document.
- Editor exports open a new tab; project editing remains modal.
- Per-user/session single instance with bounded JSON named-pipe forwarding and acknowledgement.
- Queued launch requests wait while a modal dialog is open; UI work remains on the dispatcher.
- Added launch IPC regression checks to build and installer gates.
- Kept installer identity, approved icon, project saving and previous PDF fixes.

Validation: static XML/events/source and archive checks here. Windows runtime and installer verification required on the user's computer.

# 0.6.0 - Phase 06

- Approved multi-resolution application/window icon; original PNG retained.
- Self-contained Windows x64 Release publishing script with existing clean/build/test gate.
- Inno Setup per-user installer, Start menu and optional desktop shortcuts, uninstaller.
- .hamipdf association with quoted file arguments and application-specific cleanup; no PDF association changes.
- Unique artifact directories, publish checks, build transcript and installer SHA256.
- WebView2 Runtime remains an external prerequisite. Installer is unsigned.

Static checks completed here. Windows builds, Inno compilation and installation/uninstallation were not executed here.

# 0.5.0 - Phase 05

- Portable .hamipdf projects embed source PDF bytes and editable overlays, including image assets and freehand points.
- Open project, save project, and save project as; Ctrl+S saves the project, Ctrl+Shift+S exports PDF.
- Preserve Arabic text, alignment, direction, style, opacity, positions and current page; undo history remains session-only.
- Stage and verify a temporary archive before committing. Existing projects retain one prior .bak version.
- Explicit close prompt distinguishes unsaved editable work from already exported PDFs.
- Schema, payload, geometry and image limits validated before use.
- Added project round-trip, repeated backup and failed-save preservation tests to the existing STA WPF test runner.
- Retained Phase 04 annotations, compact icons, page operations and earlier PDFsharp/restore-order fixes.

Validation here: XML/XAML, event and resource references, source review and regression guards.
Windows/.NET compilation and C#/WPF tests were not run here; build-run.ps1 runs them on the user's machine.
