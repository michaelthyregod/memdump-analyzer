# TODO

Open decisions and follow-ups. Move an item to a GitHub issue when work on it starts.

## Decisions pending

- **PDF engine.** QuestPDF's Community License is source-available, not open source: free for
  organisations under USD 1M annual gross revenue and for OSI-licensed open-source projects, but never
  for public companies or public-sector bodies. `PdfReporter` hard-codes `LicenseType.Community`, which
  self-certifies on behalf of every user. Options:
  - keep QuestPDF, and let the user state their licence (option or environment variable) instead of
    hard-coding Community;
  - print the skinned HTML to PDF with the installed Edge/Chrome (`--headless --print-to-pdf`; tested:
    1.4 s for a real report). This needs print CSS (expand `<details>`, page breaks). Skins need no
    change either way.
  - (PDFsharp/MigraDoc is AGPL from 6.5.0; MIT only up to 6.2.4.)
- **Project licence files.** MIT is decided. Still to add: `LICENSE`,
  `THIRD-PARTY-NOTICES.md` (MIT/BSD notices for the bundled packages, the QuestPDF terms, and the OFL
  for the bundled Liberation fonts), and `CONTRIBUTING.md` (contributions under MIT; the maintainer
  decides what is merged).

## Follow-ups

- Re-enable the GitHub Actions workflow (disabled manually since March 2026), then run it on `main`.
- Section headings are in title case ("Top Memory Consumers"); some brand guides ask for sentence
  case. Consider sentence case for all reports, or a skin option.
