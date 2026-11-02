# TODO

Open decisions and follow-ups. Move an item to a GitHub issue when work on it starts.

## Decisions pending

- **PDF engine (licence risk, accepted for now).** QuestPDF's Community License is source-available, not open source: free for
  organisations under USD 1M annual gross revenue and for OSI-licensed open-source projects, but never
  for public companies or public-sector bodies. `PdfReporter` hard-codes `LicenseType.Community`, which
  self-certifies on behalf of every user. Options:
  - keep QuestPDF, and let the user state their licence (option or environment variable) instead of
    hard-coding Community;
  - print the skinned HTML to PDF with the installed Edge/Chrome (`--headless --print-to-pdf`; tested:
    1.4 s for a real report). This needs print CSS (expand `<details>`, page breaks). Skins need no
    change either way.
  - (PDFsharp/MigraDoc is AGPL from 6.5.0; MIT only up to 6.2.4.)

  Accepted for now because no releases or binaries have been published. **Decide before the first
  release.**

## Follow-ups

- Re-enable the GitHub Actions workflow (disabled manually since March 2026), then run it on `main`.
