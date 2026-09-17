# Browser regression (developer-only)

Requires Node.js and Playwright in the developer environment. The ordinary Windows build-run.ps1 does not require Node.js.

Run `node run.cjs /path/to/55.pdf` from a writable working directory. Install Playwright's browser beforehand, or set HAMIPDF_CHROMIUM to a local Chromium executable.

The test is specifically for the user-supplied 70-field 55.pdf; that private PDF is not distributed in this package. Output copies/screenshots are written to form-browser-results in the working directory. It checks Arabic input, check/uncheck, save cancellation, repeated saves, reopening, and 70 widgets. It mocks only the WebView2 message bridge; it does not test WPF appearance rendering or Windows file dialogs.
