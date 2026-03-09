# report/

Generated reports are written here by default.

Each run produces one or more files named after the dump, e.g.:

```
report/
├── w3wp_20260301.html   # Interactive HTML — open in any browser
├── w3wp_20260301.json   # Machine-readable full analysis result
├── w3wp_20260301.md     # Markdown — paste into Confluence, GitHub, etc.
└── w3wp_20260301.pdf    # Printable / shareable PDF
```

Override the output directory with `--output`:

```bash
dotnet run --project src/MemDumpAnalyzer.Cli -- analyze mem-dumps/dump.dmp --output ./reports/2026-03-09
```

## Format guide

| Format | Best for |
|--------|----------|
| `html` | Day-to-day investigation — collapsible sections, full thread stacks, colour-coded severity |
| `pdf`  | Sharing with stakeholders or attaching to incident tickets |
| `md`   | Pasting into Confluence, GitHub issues, or ADO work items |
| `json` | Feeding into dashboards, diffing two results programmatically, or scripting follow-up queries |

Specify multiple formats in one run:

```bash
dotnet run --project src/MemDumpAnalyzer.Cli -- analyze mem-dumps/dump.dmp --format html,json,md,pdf
```

## Notes

- Report files are excluded from Git (see `.gitignore`) — they are fully reproducible from the source dump.
- The `html` report is self-contained (no external dependencies) and can be sent as a single file attachment.
