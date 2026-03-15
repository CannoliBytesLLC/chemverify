param(
    [string]$OutputPath = "output/pdf/chemverify-app-summary.pdf"
)

$ErrorActionPreference = "Stop"

$culture = [System.Globalization.CultureInfo]::InvariantCulture
$pageHeight = 792.0

function Format-Number {
    param([double]$Value)
    $Value.ToString("0.##", $culture)
}

function Escape-PdfText {
    param([string]$Text)
    $Text.Replace("\", "\\").Replace("(", "\(").Replace(")", "\)").Replace("`r", " ").Replace("`n", " ")
}

function Wrap-Text {
    param(
        [string]$Text,
        [int]$MaxChars
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return @("")
    }

    $words = $Text -split "\s+"
    $lines = New-Object System.Collections.Generic.List[string]
    $current = ""

    foreach ($word in $words) {
        if ([string]::IsNullOrEmpty($current)) {
            $current = $word
            continue
        }

        if (($current.Length + 1 + $word.Length) -le $MaxChars) {
            $current = "$current $word"
        }
        else {
            $lines.Add($current) | Out-Null
            $current = $word
        }
    }

    if (-not [string]::IsNullOrEmpty($current)) {
        $lines.Add($current) | Out-Null
    }

    $lines.ToArray()
}

$commands = New-Object System.Collections.Generic.List[string]

function Add-Command {
    param([string]$Command)
    $script:commands.Add($Command) | Out-Null
}

function Add-FillRect {
    param(
        [double]$X,
        [double]$Top,
        [double]$Width,
        [double]$Height,
        [double]$R,
        [double]$G,
        [double]$B
    )

    $pdfY = $script:pageHeight - $Top - $Height
    Add-Command ("{0} {1} {2} rg {3} {4} {5} {6} re f" -f (Format-Number $R), (Format-Number $G), (Format-Number $B), (Format-Number $X), (Format-Number $pdfY), (Format-Number $Width), (Format-Number $Height))
}

function Add-Line {
    param(
        [double]$X1,
        [double]$Top1,
        [double]$X2,
        [double]$Top2,
        [double]$R,
        [double]$G,
        [double]$B,
        [double]$Width = 1
    )

    $y1 = $script:pageHeight - $Top1
    $y2 = $script:pageHeight - $Top2
    Add-Command ("{0} {1} {2} RG {3} w {4} {5} m {6} {7} l S" -f (Format-Number $R), (Format-Number $G), (Format-Number $B), (Format-Number $Width), (Format-Number $X1), (Format-Number $y1), (Format-Number $X2), (Format-Number $y2))
}

function Add-Text {
    param(
        [double]$X,
        [double]$Top,
        [string]$Font,
        [double]$Size,
        [double]$R,
        [double]$G,
        [double]$B,
        [string]$Text
    )

    $escaped = Escape-PdfText $Text
    $pdfY = $script:pageHeight - $Top
    Add-Command ("BT /{0} {1} Tf {2} {3} {4} rg 1 0 0 1 {5} {6} Tm ({7}) Tj ET" -f $Font, (Format-Number $Size), (Format-Number $R), (Format-Number $G), (Format-Number $B), (Format-Number $X), (Format-Number $pdfY), $escaped)
}

function Add-SectionHeading {
    param(
        [double]$X,
        [ref]$TopRef,
        [double]$Width,
        [string]$Title
    )

    Add-Text -X $X -Top $TopRef.Value -Font "F2" -Size 11 -R 0.10 -G 0.19 -B 0.31 -Text $Title.ToUpperInvariant()
    $TopRef.Value += 6
    Add-Line -X1 $X -Top1 $TopRef.Value -X2 ($X + $Width) -Top2 $TopRef.Value -R 0.82 -G 0.86 -B 0.91 -Width 0.9
    $TopRef.Value += 14
}

function Add-Paragraph {
    param(
        [double]$X,
        [ref]$TopRef,
        [double]$Size,
        [double]$Leading,
        [int]$MaxChars,
        [string]$Text
    )

    foreach ($line in (Wrap-Text -Text $Text -MaxChars $MaxChars)) {
        Add-Text -X $X -Top $TopRef.Value -Font "F1" -Size $Size -R 0.14 -G 0.17 -B 0.22 -Text $line
        $TopRef.Value += $Leading
    }

    $TopRef.Value += 6
}

function Add-Bullets {
    param(
        [double]$X,
        [ref]$TopRef,
        [double]$Size,
        [double]$Leading,
        [int]$MaxChars,
        [string[]]$Items
    )

    foreach ($item in $Items) {
        $wrapped = Wrap-Text -Text $item -MaxChars $MaxChars
        $first = $true

        foreach ($line in $wrapped) {
            $prefix = if ($first) { "- " } else { "  " }
            Add-Text -X $X -Top $TopRef.Value -Font "F1" -Size $Size -R 0.14 -G 0.17 -B 0.22 -Text ($prefix + $line)
            $TopRef.Value += $Leading
            $first = $false
        }

        $TopRef.Value += 2
    }

    $TopRef.Value += 4
}

$leftColumnX = 42.0
$rightColumnX = 323.0
$columnWidth = 247.0
$leftTop = 124.0
$rightTop = 124.0

$whatItIs = @(
    "ChemVerify is a deterministic verification engine for chemistry procedure text, especially AI-generated output.",
    "It checks whether a procedure is internally consistent, sufficiently specified, and computationally interpretable before experimental use."
) -join " "

$whoItsFor = "Primary persona: a chemist or lab reviewer who needs to vet AI- or human-written procedures before acting on them."

$featureBullets = @(
    "Verifies supplied text through the API /verify endpoint and the CLI analyze command.",
    "Supports a generate-and-verify API flow through /runs using a MockModelConnector.",
    "Extracts DOI, numeric-unit, and reagent-role claims from procedure text.",
    "Runs auto-discovered validators for contradictions, missing conditions, malformed scientific claims, and reagent-solvent issues.",
    "Applies policy profiles from PolicyProfiles.json to change contract rules and validator coverage.",
    "Produces deterministic reports with severity, verdict, risk drivers, next questions, and evidence spans.",
    "Persists runs, claims, and findings in SQLite via EF Core, and serves Swagger plus browser report pages."
)

$architectureBullets = @(
    "Entrypoints: static web pages in ChemVerify.API/wwwroot, REST endpoints in ChemVerify.API, and ChemVerify.Cli.",
    "API calls AuditService; CLI uses AnalyzeCommandHandler, which mirrors the verify-only pipeline without infrastructure.",
    "GenerateAndVerify uses MockModelConnector; VerifyOnly analyzes the supplied text directly.",
    "CompositeClaimExtractor combines DoiClaimExtractor, NumericUnitExtractor, and ReagentRoleExtractor.",
    "Assembly-scanned IValidator implementations create findings; EvidenceLocator, RiskScorer, ReportBuilder, and ProcedureSummaryBuilder shape the output.",
    "API persistence flows through ChemVerifyRunRepository and ChemVerifyDbContext to SQLite (chemverify.db)."
)

$runBullets = @(
    "Install a .NET SDK that can build the repo's net10.0 projects. README still says .NET 8 recommended.",
    "From the repo root: dotnet restore",
    "Start the API: dotnet run --project ChemVerify.API",
    "Open http://localhost:5028, https://localhost:7033, or /swagger.",
    "CLI example: dotnet run --project ChemVerify.Cli -- analyze .\\ChemVerify.Tests\\TestData\\Input\\CleanProcedure.txt --format json"
)

Add-FillRect -X 36 -Top 34 -Width 540 -Height 68 -R 0.10 -G 0.19 -B 0.31
Add-Text -X 48 -Top 63 -Font "F2" -Size 23 -R 1 -G 1 -B 1 -Text "ChemVerify"
Add-Text -X 48 -Top 84 -Font "F1" -Size 10.2 -R 0.86 -G 0.91 -B 0.97 -Text "One-page repo summary"
Add-Text -X 420 -Top 63 -Font "F2" -Size 10 -R 0.83 -G 0.91 -B 1 -Text "Status"
Add-Text -X 420 -Top 81 -Font "F1" -Size 10 -R 1 -G 1 -B 1 -Text "Prototype / reference implementation"

Add-Line -X1 306 -Top1 122 -X2 306 -Top2 742 -R 0.89 -G 0.91 -B 0.94 -Width 0.8

Add-SectionHeading -X $leftColumnX -TopRef ([ref]$leftTop) -Width $columnWidth -Title "What It Is"
Add-Paragraph -X $leftColumnX -TopRef ([ref]$leftTop) -Size 9.3 -Leading 11.4 -MaxChars 48 -Text $whatItIs

Add-SectionHeading -X $leftColumnX -TopRef ([ref]$leftTop) -Width $columnWidth -Title "Who It's For"
Add-Paragraph -X $leftColumnX -TopRef ([ref]$leftTop) -Size 9.3 -Leading 11.4 -MaxChars 48 -Text $whoItsFor

Add-SectionHeading -X $leftColumnX -TopRef ([ref]$leftTop) -Width $columnWidth -Title "What It Does"
Add-Bullets -X $leftColumnX -TopRef ([ref]$leftTop) -Size 8.7 -Leading 10.6 -MaxChars 43 -Items $featureBullets

Add-SectionHeading -X $rightColumnX -TopRef ([ref]$rightTop) -Width $columnWidth -Title "How It Works"
Add-Bullets -X $rightColumnX -TopRef ([ref]$rightTop) -Size 8.6 -Leading 10.4 -MaxChars 42 -Items $architectureBullets

Add-SectionHeading -X $rightColumnX -TopRef ([ref]$rightTop) -Width $columnWidth -Title "How To Run"
Add-Bullets -X $rightColumnX -TopRef ([ref]$rightTop) -Size 8.6 -Leading 10.4 -MaxChars 42 -Items $runBullets

Add-FillRect -X 36 -Top 750 -Width 540 -Height 16 -R 0.96 -G 0.97 -B 0.98
Add-Text -X 42 -Top 761 -Font "F1" -Size 7.8 -R 0.36 -G 0.40 -B 0.46 -Text "Summary derived from README, current .NET projects, API wiring, core pipeline services, and persistence code."

$streamContent = ($commands -join "`n") + "`n"
$length = [System.Text.Encoding]::ASCII.GetByteCount($streamContent)
$contentObject = "4 0 obj`n<< /Length $length >>`nstream`n$streamContent" + "endstream`nendobj`n"

$objects = @(
    "1 0 obj`n<< /Type /Catalog /Pages 2 0 R >>`nendobj`n",
    "2 0 obj`n<< /Type /Pages /Count 1 /Kids [3 0 R] >>`nendobj`n",
    "3 0 obj`n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> /Contents 4 0 R >>`nendobj`n",
    $contentObject,
    "5 0 obj`n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>`nendobj`n",
    "6 0 obj`n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>`nendobj`n"
)

$output = New-Object System.Text.StringBuilder
[void]$output.Append("%PDF-1.4`n")
$offsets = @()
$currentOffset = [System.Text.Encoding]::ASCII.GetByteCount("%PDF-1.4`n")

foreach ($object in $objects) {
    $offsets += $currentOffset
    [void]$output.Append($object)
    $currentOffset += [System.Text.Encoding]::ASCII.GetByteCount($object)
}

$xrefOffset = $currentOffset
$xref = New-Object System.Text.StringBuilder
[void]$xref.Append("xref`n")
[void]$xref.Append("0 $($objects.Count + 1)`n")
[void]$xref.Append("0000000000 65535 f `n")

foreach ($offset in $offsets) {
    [void]$xref.AppendFormat($culture, "{0:0000000000} 00000 n `n", $offset)
}

[void]$xref.Append("trailer`n")
[void]$xref.Append("<< /Size $($objects.Count + 1) /Root 1 0 R >>`n")
[void]$xref.Append("startxref`n")
[void]$xref.Append("$xrefOffset`n")
[void]$xref.Append("%%EOF")
[void]$output.Append($xref.ToString())

$fullOutputPath = Join-Path (Get-Location) $OutputPath
$outputDir = Split-Path -Parent $fullOutputPath
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
}

[System.IO.File]::WriteAllBytes($fullOutputPath, [System.Text.Encoding]::ASCII.GetBytes($output.ToString()))
Write-Output $fullOutputPath
