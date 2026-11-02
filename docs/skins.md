# Report skins

A skin sets the look and feel of the HTML, PDF and diff reports (colours, fonts, logo) and the
texts around them (title, company, prepared for/by, footer). Markdown gets the texts only;
JSON is data and is never skinned.

```bash
MemDumpAnalyzer.Cli.exe analyze dump.dmp --skin northwind --prepared-for "Example Customer A/S"
MemDumpAnalyzer.Cli.exe analyze dump.dmp --skin C:\path\to\my-skin
MemDumpAnalyzer.Cli.exe diff a.dmp b.dmp --skin C:\path\to\my-skin
```

Built-in skins: `default` (the original look) and `northwind` (a reference skin for a fictional
company that uses every option). Any other value is a folder containing `skin.json`.

The skin is independent of how a report is drawn: the HTML and the PDF read the same skin.

## Folder layout

```
my-skin/
├── skin.json       # required
├── logo.svg        # optional: .svg, .png or .jpg
└── fonts/          # optional: the skin's own font files
```

Files are only read from inside the skin folder.

## skin.json

Every part is optional — anything left out comes from the default skin. Comments (`//`) and
trailing commas are allowed. See `src/MemDumpAnalyzer.Reporting/Skins/BuiltIn/northwind/skin.json`
for a complete example.

```jsonc
{
  "name": "acme",
  "displayName": "ACME",
  "texts": {
    "reportTitle": "Memory dump health report",  // default "Memory Dump Analysis Report"
    "companyName": "ACME",                         // under the title
    "preparedBy": "ACME Performance Team",
    "preparedFor": "…",                            // usually set per report with --prepared-for
    "footer": "ACME"                               // "memdump-analyzer" is always appended
  },
  "logo": "logo.svg",
  "logoHeightMm": 12,        // 5–60; width follows the logo's own proportions (never distorted),
                             // with half the height as clear space
  "headingWeight": 700,      // 400, 600 or 700
  "colors": { "primary": "#1f4e5f" },
  "fonts": {
    "body":    [ "ACME Sans", "Arial" ],
    "heading": [ "ACME Sans", "Arial" ],   // defaults to body
    "mono":    [ "ACME Mono", "Courier New" ]
  },
  "fontFiles": [
    { "family": "ACME Sans", "file": "fonts/AcmeSans-Regular.ttf", "weight": 400 },
    { "family": "ACME Sans", "file": "fonts/AcmeSans-Bold.ttf",    "weight": 700 },
    { "family": "ACME Sans", "file": "fonts/AcmeSans-Italic.ttf",  "weight": 400, "style": "italic" }
  ],
  "fontLicense": "What you cleared for these fonts"
}
```

Problems (unknown colour names, bad hex values, missing files) are printed as warnings and the
default value is used; a missing skin or invalid JSON stops the run.

### Colours

All values are `#RRGGBB`.

| Token | Used for |
|---|---|
| `background` | Page background |
| `surface` | Cards, tables |
| `text`, `heading` | Body text; title and key values |
| `primary`, `onPrimary` | Section headings, table headers, bars; text on them and on severity badges |
| `accent` | Recommendations, links |
| `border`, `divider`, `stripe` | Heading underline and card edges; table rows and bar tracks; alternate rows |
| `muted`, `subtle` | Secondary text; footer and hints |
| `critical`, `warning`, `info`, `success` (+ `…Background`) | Severity colours and finding backgrounds |
| `codeBackground`, `codeHeader`, `codeText`, `codeAccent` | Technical details blocks |
| `stackBackground` | Inline stack traces |

### Fonts

Each role (`body`, `heading`, `mono`) is an ordered list; the first family that can be found wins:

1. the skin's own files (`fontFiles`, `.ttf`/`.otf` for HTML and PDF, `.woff2`/`.woff` for HTML only),
2. fonts installed on the machine (PDF) or the reader's browser (HTML),
3. the built-in fallbacks **Liberation Sans** and **Liberation Mono** (SIL OFL 1.1), metric-compatible
   with Arial and Courier New, so the layout holds everywhere.

A warning is printed when a role's first choice isn't available. In the PDF, the font is known by the
family name stored inside the font file; `family` in `fontFiles` is just the label the skin uses.

**Licensing is the skin author's responsibility.** HTML reports embed the skin's font files
(`@font-face`), and PDFs embed the glyphs used. Many commercial font licences don't allow either, so
check before shipping a font with a skin, and record what was cleared in `fontLicense`.
Open-source fonts under the SIL Open Font License allow embedding.

### Brand assets

Logos and brand colours belong to their owners. Use a company's brand in a skin only with their
permission, and never make a report look as if it came from someone else.
