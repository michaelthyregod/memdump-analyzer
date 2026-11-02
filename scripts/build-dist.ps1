<#
.SYNOPSIS
    Builds memdump-analyzer-win-x64.zip: one self-contained Native AOT exe plus the files a user needs.

.DESCRIPTION
    Publishes the CLI with -p:SingleExe=true (QuestPDF's native DLLs embedded in the exe), then
    assembles:
        MemDumpAnalyzer.Cli.exe, README-FIRST.txt, known-assemblies.txt,
        Readme.md, LICENSE, THIRD-PARTY-NOTICES.md, docs\usage.md, docs\skins.md,
        mem-dumps\README.md (drop dumps here), report\ (default output),
        skins\ (the repo's example skins, plus any from -SkinsDir)
    The zip is git-ignored; it is not a public release.

.PARAMETER SkinsDir
    Optional folder whose skin sub-folders (each with a skin.json) are copied to skins\ in the zip,
    together with its README.md. Use it for skins that must not live in this repo.

.PARAMETER Output
    Zip path. Default: memdump-analyzer-win-x64.zip in the repo root.

.EXAMPLE
    .\scripts\build-dist.ps1
    .\scripts\build-dist.ps1 -SkinsDir ..\my-company-skins
#>
[CmdletBinding()]
param(
    [string] $SkinsDir,
    [string] $Output,
    [string] $RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Output) { $Output = Join-Path $repo "memdump-analyzer-$RuntimeIdentifier.zip" }

# Native AOT linking needs vswhere.exe, which lives with the Visual Studio Installer
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if ((Test-Path $vsInstaller) -and ($env:PATH -notlike "*$vsInstaller*")) { $env:PATH = "$vsInstaller;$env:PATH" }

$work = Join-Path ([IO.Path]::GetTempPath()) "memdump-analyzer-dist-$([Guid]::NewGuid().ToString('N'))"
$publish = Join-Path $work 'publish'
$stage = Join-Path $work 'stage'
try {
    Write-Host "Publishing $RuntimeIdentifier single exe..."
    dotnet publish (Join-Path $repo 'src\MemDumpAnalyzer.Cli') -c Release -r $RuntimeIdentifier -p:SingleExe=true -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

    New-Item -ItemType Directory -Force $stage, "$stage\docs", "$stage\mem-dumps", "$stage\report" | Out-Null
    Copy-Item "$publish\MemDumpAnalyzer.Cli.exe" $stage
    foreach ($f in 'known-assemblies.txt', 'Readme.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md') { Copy-Item (Join-Path $repo $f) $stage }
    Copy-Item (Join-Path $repo 'docs\usage.md'), (Join-Path $repo 'docs\skins.md') "$stage\docs"
    Copy-Item (Join-Path $repo 'mem-dumps\README.md') "$stage\mem-dumps"
    Set-Content -Path "$stage\report\here comes your reports.txt" -Value 'Reports are written here by default.' -NoNewline

    $commit = (git -C $repo rev-parse --short HEAD).Trim()
    if (git -C $repo status --porcelain) { $commit += ' (with uncommitted changes)' }
    (Get-Content (Join-Path $repo 'dist\README-FIRST.txt') -Raw) `
        -replace '\{\{COMMIT\}\}', "commit $commit" `
        -replace '\{\{DATE\}\}', (Get-Date -Format 'yyyy-MM-dd') |
        Set-Content "$stage\README-FIRST.txt" -NoNewline

    # The repo's example skins (northwind) always ship; -SkinsDir adds more
    New-Item -ItemType Directory -Force "$stage\skins" | Out-Null
    $sources = @(Join-Path $repo 'skins')
    if ($SkinsDir) { $sources += (Resolve-Path $SkinsDir).Path }
    $shipped = @()
    foreach ($source in $sources) {
        $skins = Get-ChildItem $source -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'skin.json') }
        if (-not $skins) { throw "No skin folders (with skin.json) in $source" }
        foreach ($s in $skins) {
            if (Test-Path "$stage\skins\$($s.Name)") { throw "Two skins named '$($s.Name)'" }
            Copy-Item $s.FullName "$stage\skins\$($s.Name)" -Recurse
            $shipped += $s.Name
        }
        if (Test-Path "$source\README.md") { Copy-Item "$source\README.md" "$stage\skins" }
    }
    Write-Host "Skins: $($shipped -join ', ')"

    if (Test-Path $Output) { Remove-Item $Output -Confirm:$false }
    Compress-Archive -Path "$stage\*" -DestinationPath $Output -CompressionLevel Optimal
    Write-Host ("Wrote {0} ({1:N1} MB)" -f $Output, ((Get-Item $Output).Length / 1MB))
}
finally {
    if (Test-Path $work) { Remove-Item $work -Recurse -Force -Confirm:$false }
}
