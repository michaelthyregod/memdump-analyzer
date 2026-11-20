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

## Report structure (feedback from first users)

Items 1–3 (index of sections, conclusions-first order, cross-links) are done: `ReportLayout`.

- **Hotspots: say who can change the code** (idea phase — needs to be made concrete). Today the
   assembly filter only hides or shows. Instead, mark each hotspot with who owns it, e.g. *your code*,
   *platform* (`Sitecore.*`), *third party* (`Microsoft.ApplicationInsights.*`, `StackExchange.Redis`),
   *.NET framework*, so a reader sees at once that a platform hotspot can't be fixed in their own code
   (but may still be configured, patched or reported to the vendor). Open questions:
   - grouping in the filter file (e.g. named groups or sections instead of one flat list);
   - different filters per audience (developer, operations, customer/management) — chosen with an
     option, a setting or the skin;
   - whether hidden assemblies should still appear, collapsed under their group;
   - how findings use it (e.g. lock contention inside Sitecore vs. inside custom code).

## Analysis

- **Retention paths for the largest types** (replacement for the old GC-root sampling, now opt-in via
  `--gc-roots`). Show *who keeps* a big object alive — the chain object ← owner ← … ← static field,
  handle or thread — like WinDbg `!gcroot`, which is what explains a leak or an oversized cache. The old
  sampling only matched objects that were themselves roots (almost never) and walked every thread's
  stack roots (~0.6 s per thread, over an hour on a 7,500-thread dump). Make it bounded: a few sample
  objects of the top type(s) only, handle and static roots before stack roots, a time limit with a
  clear "partial" note, and opt-in or limited by default. ClrMD has building blocks for this (GCRoot).

## Follow-ups

- Re-enable the GitHub Actions workflow (disabled manually since March 2026), then run it on `main`.
