# Third-party notices

memdump-analyzer is licensed under the [MIT License](LICENSE). It uses the third-party components
below, each under its own licence. Anyone who distributes the compiled tool (for example a published
`MemDumpAnalyzer.Cli.exe`) must include this file and the licence texts it refers to.

Build- and test-only packages (the .NET SDK and ILCompiler, xunit, Microsoft.NET.Test.Sdk, coverlet)
are not part of the compiled tool and are not listed.

## QuestPDF — QuestPDF Community / Professional License (not an open-source licence)

- Package: QuestPDF 2026.9.1 — <https://www.questpdf.com/> — © Marcin Ziąbek, QuestPDF contributors
- Licence: <https://www.questpdf.com/license/> (the licence text shipped with each release governs that release)

QuestPDF produces the PDF report. **Its licence is source-available, not open source**, and it applies
to the organisation that uses it. The Community License is free for, among others, individuals and
organisations with annual gross revenue under USD 1,000,000, open-source projects under an
OSI-approved licence, and qualifying non-profit and academic use. Public companies and public-sector
bodies are not eligible regardless of revenue. **If your organisation doesn't qualify, you need a paid
QuestPDF licence to use the PDF output** — or use the HTML, Markdown and JSON reports, which don't use
QuestPDF. Check the current terms yourself; this summary is not legal advice.

> **Known issue:** `PdfReporter` currently sets `QuestPDF.Settings.License = LicenseType.Community`
> for every user. Whether to keep QuestPDF (and let users state their own licence) or replace it is an
> open decision — see [TODO.md](TODO.md). No releases or binaries have been published so far.

QuestPDF bundles native components (Skia, HarfBuzz, FreeType, qpdf, libpng, libjpeg-turbo, libwebp,
zlib, expat, wuffs, libgrapheme and the Lato font) under their own licences, which are included in the
`ExternalDependencyLicenses` folder of the QuestPDF NuGet package.

## Liberation fonts — SIL Open Font License 1.1

- Liberation Sans (Regular, Bold, Italic, Bold Italic), Liberation Mono (Regular, Bold) and, in the
  Northwind reference skin, Liberation Serif Bold — version 2.1.5 — <https://github.com/liberationfonts/liberation-fonts>
- Copyright (c) 2010 Google Corporation with Reserved Font Arimo, Tinos and Cousine;
  Copyright (c) 2012 Red Hat, Inc. with Reserved Font Name Liberation.
- Embedded in the tool as the built-in fallback fonts for report skins. Full licence text:
  [src/MemDumpAnalyzer.Reporting/Skins/Fonts/LICENSE-Liberation-OFL.txt](src/MemDumpAnalyzer.Reporting/Skins/Fonts/LICENSE-Liberation-OFL.txt)

## Scriban — BSD 2-Clause License

- Package: Scriban 7.5.3 — <https://github.com/scriban/scriban> — Copyright (c) Alexandre Mutel

```
Redistribution and use in source and binary forms, with or without modification, are permitted
provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions
   and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of
   conditions and the following disclaimer in the documentation and/or other materials provided
   with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR
IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR
CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER
IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF
THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## MIT-licensed components

| Component | Version | Copyright | Project |
|---|---|---|---|
| Microsoft.Diagnostics.Runtime (ClrMD) | 4.2.751201 | © Microsoft Corporation | <https://github.com/microsoft/clrmd> |
| Microsoft.Diagnostics.NETCore.Client | 0.2.661903 | © Microsoft Corporation | <https://github.com/dotnet/diagnostics> |
| System.CommandLine | 3.0.0-rc.3 | © Microsoft Corporation | <https://github.com/dotnet/command-line-api> |
| Spectre.Console, Spectre.Console.Ansi | 0.57.3-alpha.0.29 | © Patrik Svensson, Phil Scott, Nils Andresen, Cédric Luthi | <https://github.com/spectreconsole/spectre.console> |
| Azure.Core, Azure.Identity, System.ClientModel (via ClrMD) | 1.53.0, 1.21.0, 1.10.0 | © Microsoft Corporation | <https://github.com/Azure/azure-sdk-for-net> |
| Microsoft.Identity.Client, Microsoft.Identity.Client.Extensions.Msal, Microsoft.IdentityModel.Abstractions (via ClrMD) | 4.83.1, 4.83.1, 8.14.0 | © Microsoft Corporation | <https://github.com/AzureAD> |
| Microsoft.Security.AntiSSRF (via ClrMD) | 1.0.0 | © Microsoft Corporation | <https://github.com/microsoft/AntiSSRF> |
| Microsoft.Extensions.* abstractions, Microsoft.Bcl.AsyncInterfaces, System.Memory.Data, System.Security.Cryptography.ProtectedData | 10.0.3 / 4.5.0 | © Microsoft Corporation | <https://github.com/dotnet/runtime> |
| .NET runtime (compiled into the Native AOT executable) | 10.0 | © .NET Foundation and Contributors | <https://github.com/dotnet/runtime> |

```
MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute,
sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES
OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## Content

The analysis heuristics build on Nikolay Mitikov's blog <https://makingitfaster.tips/> and his work at
<https://github.com/mitikov>. Short passages are quoted with attribution; no code from those sources is
included.
