# Browser regression (developer-only)

Requires Node.js and Playwright in the developer environment. The ordinary Windows build-run.ps1 does not require Node.js.

Run `node run.cjs /path/to/55.pdf` from a writable working directory. Install Playwright's browser beforehand, or set HAMIPDF_CHROMIUM to a local Chromium executable.

The test is specifically for the user-supplied 70-field 55.pdf; that private PDF is not distributed in this package. Output copies/screenshots are written to form-browser-results in the working directory. It checks Arabic input, check/uncheck, save cancellation, repeated saves, reopening, and 70 widgets. It mocks only the WebView2 message bridge; it does not test WPF appearance rendering or Windows file dialogs.


## Form page composition (0.9.2)

`FormPageChecks prepare plan.json input.pdf 1:90,0 other.pdf 0:180` creates a plan with zero-based pages and added rotations.
`node pages.cjs plan.json composed.pdf` runs the actual bundled JS engine in Playwright Chromium.
`FormPageChecks finish plan.json composed.pdf final.pdf` applies rotations and validates the canonical field tree, values and appearances before creating a new PDF.
`node refill-pages.cjs merged-55.pdf saved.pdf` tests a two-source merge of 55.pdf for independent editable values, including Arabic.
Set HAMIPDF_CHROMIUM to an explicit browser executable if needed. The portable C# regression fixtures are synthetic; they contain no user document.
