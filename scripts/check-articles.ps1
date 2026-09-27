<#
.SYNOPSIS
  Check article drafts before anyone reads them: dead links, numbers that aren't in the linked report,
  banned terms and prose style. Prints each problem as `file:line: message`. Exits 0 when clean, 1 otherwise.

.DESCRIPTION
  Four checks, run over every Markdown file given (FR-008):

  1. Links. Every Markdown link `[text](target)` and image `![alt](target)` whose target is not a URL must
     point at a file or folder that exists. A target is tried relative to the article's own folder
     (so `../../examples/banking77/report.json` works) and then relative to the repo root. A target that
     starts with `/` is resolved against the repo root. `#anchor` and `?query` parts are dropped. Reference
     links (`[text][ref]` with a `[ref]: target` line) are handled the same way. http(s) links are not
     fetched: they are listed in a summary at the end.

  2. Numbers. Prose is split into sentences (table rows, headings and list items are units of their own).
     For every sentence that links at least one existing report file (.json, .md, .html), each number in the
     sentence's prose must appear in at least one of the files it links. The link target is never read for
     numbers, nor is link text that is itself a path. Candidate values are every number token in the linked
     file, read as raw text (so JSON and prose both work). A number with d decimals matches a file value v:
       - percentage p (73.6%, 94.2 per cent): round(v*100, d) == p, or round(v, d) == p when the file
         already stores a percentage;
       - plain number, including 1,000 and £/$/€ amounts: round(v, d) == the number;
       - 22.7M / 3k / 1.2B / 1.2bn: round(v / 1e6 (1e3, 1e9), d) == the number, or round(v, d) == it.
     "round" here means within half a unit of the last stated digit, so either rounding rule passes. Signs are
     ignored. A sentence with numbers but no report link is fine; it is listed under "unlinked numbers" when
     the script runs with -Verbose so the author can review it.

     Exempt from the number check (see $Exemptions below, which is the single source of these rules):
       - anything in code: fenced blocks, `inline code`, HTML comments and YAML front matter;
       - ISO dates (2026-09-27), day-month and month-day dates (27 September 2026, Sept 27), month-year;
       - years 1990-2039 written as four bare digits;
       - version numbers with two or more dots (1.2.3, v0.3.20);
       - GPU names (RTX 3080 Ti, GTX 1080, RX 7900 XT);
       - model names followed by a number (Opus 5.5, Sonnet 4.6, GPT-4, Llama 3, Qwen 2.5, ...);
       - identifiers that start with a letter and join a digit with a hyphen (von-1.2.0, all-MiniLM-L6-v2,
         gpt-4o, laya-en-ft-banking77, FR-008, top-5);
       - .NET versions (.NET 10) and file names (report.json);
       - digits glued to letters are never read as numbers (Banking77, L6, R2, net10.0), apart from the
         suffixes %, M, k, B, bn, x, ms, s, pp, GB, MB, W, Wh and kWh.

  3. Banned terms. Case-insensitive, whole word, anywhere in the file including code. The list comes from
     -Banned. Extra terms are passed at run time and are never written into the repo.

  4. Prose style, outside code and link targets: an em dash is an error, an en dash used as a dash (with a
     space either side) is an error, and a semicolon is an error. HTML entities such as &amp; are ignored.

.PARAMETER Path
  Folder of drafts. Every *.md directly inside it is checked. Default: docs/articles (relative to the repo root
  when it isn't found relative to the current folder).

.PARAMETER Files
  Check these Markdown files instead of -Path. Accepts an array or a comma-separated list.

.PARAMETER Banned
  Whole words that must not appear. Accepts an array or a comma-separated list. Replaces the default list, so
  pass the defaults again when adding a name.

.EXAMPLE
  ./scripts/check-articles.ps1
.EXAMPLE
  ./scripts/check-articles.ps1 -Files docs/articles/hn.md,docs/articles/reddit.md -Verbose
.EXAMPLE
  ./scripts/check-articles.ps1 -Banned vehicle,vehicles,<extra-term>
#>
[CmdletBinding()]
param(
    [string] $Path = 'docs/articles',
    [string[]] $Files,
    [string[]] $Banned = @('fleet', 'fleets', 'vehicle', 'vehicles', 'telematics')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/..").Path
$inv = [Globalization.CultureInfo]::InvariantCulture

# ---------- rules (data) ----------

$Months = 'Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?'
# Spans matching any of these are blanked before numbers are read. Case-sensitive unless the pattern says (?i).
$Exemptions = @(
    @{ Name = 'ISO date';              Pattern = '\b\d{4}-\d{2}-\d{2}(?:T[\d:.]+Z?)?\b' }
    @{ Name = 'day-month date';        Pattern = "\b\d{1,2}(?:st|nd|rd|th)?\s+(?:$Months)\b(?:,?\s+\d{4})?" }
    @{ Name = 'month-day date';        Pattern = "\b(?:$Months)\.?\s+\d{1,2}(?:st|nd|rd|th)?\b(?:,?\s+\d{4})?" }
    @{ Name = 'month and year';        Pattern = "\b(?:$Months)\.?\s+\d{4}\b" }
    @{ Name = 'year';                  Pattern = '(?<![\d.,£$€])\b(?:199\d|20[0-3]\d)\b(?![.,]\d|\s?%|\s+per\s+cent)' }
    @{ Name = 'version';               Pattern = '\bv?\d+(?:\.\d+){2,}\b' }
    @{ Name = 'GPU';                   Pattern = '\b(?:RTX|GTX|RX)\s?\d{3,4}(?:\s?(?:Ti|SUPER|Super|XTX|XT))?\b' }
    @{ Name = 'model name';            Pattern = '(?i)\b(?:Opus|Sonnet|Haiku|Claude|GPT|Gemini|Gemma|Llama|Qwen|Mistral|Phi|DeepSeek)[\s-]?\d+(?:\.\d+)*[a-z]*\b' }
    @{ Name = 'hyphenated identifier'; Pattern = '\b[A-Za-z][\w.]*-[\w.-]*\d[\w.-]*|\b[A-Za-z]\w*\d\w*-[\w.-]*' }
    @{ Name = '.NET version';          Pattern = '\.NET\s?\d+(?:\.\d+)?' }
    @{ Name = 'file name';             Pattern = '[\w./-]+\.(?:json|md|html|png|jpg|svg|csv|jsonl|ps1|cs|csproj|py|onnx|txt)\b' }
)
$NumberRx = [regex] ('(?<![\w.,\-])(?<cur>[£$€])?(?<num>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)' +
    '(?:(?<pct>\s?%|\s+per\s+cent\b)|(?<mul>bn|[MkB])\b|(?:ms|µs|pp|GB|MB|kWh|Wh|W|s|x)\b|×)?(?![\w%]|[.,]\d)')
$FileNumberRx = [regex] '\d{1,3}(?:,\d{3})+(?:\.\d+)?(?!\d)|\d+(?:\.\d+)?(?:[eE][-+]?\d+)?'
$ReportExt = @('.json', '.md', '.html')
$Abbreviations = @('e.g', 'i.e', 'vs', 'etc', 'cf', 'approx', 'fig', 'al', 'mr', 'mrs', 'dr')
$LinkRx = [regex] '(?<img>!?)\[(?<text>(?:[^\[\]]|\[[^\]]*\])*)\]\(\s*<?(?<target>[^)\s>]+)>?(?:\s+"[^"]*")?\s*\)'
$RefLinkRx = [regex] '(?<img>!?)\[(?<text>(?:[^\[\]]|\[[^\]]*\])*)\]\[(?<ref>[^\]]*)\]'
$RefDefRx = [regex] '(?m)^[ ]{0,3}\[(?<ref>[^\]]+)\]:[ \t]*<?(?<target>[^\s>]+)>?.*$'
$UrlRx = [regex] '\bhttps?://[^\s)>\]]+'

# ---------- helpers ----------

function Split-List([string[]] $items) { @($items | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }

function Get-DisplayPath([string] $full) {
    $rel = [IO.Path]::GetRelativePath($root, $full)
    if ($rel.StartsWith('..')) { return $full }
    return $rel.Replace('\', '/')
}

function Clear-Span([char[]] $chars, [int] $start, [int] $length) {
    for ($i = $start; $i -lt $start + $length -and $i -lt $chars.Length; $i++) { if ($chars[$i] -ne "`n") { $chars[$i] = ' ' } }
}

function Get-LineNumber([int[]] $starts, [int] $offset) {
    $lo = 0; $hi = $starts.Length - 1
    while ($lo -lt $hi) { $mid = [int][math]::Ceiling(($lo + $hi) / 2); if ($starts[$mid] -le $offset) { $lo = $mid } else { $hi = $mid - 1 } }
    return $lo + 1
}

# Blank everything that isn't prose: front matter, fenced code, HTML comments, inline code, entities.
function Get-MaskedText([string] $text) {
    $chars = $text.ToCharArray()
    $fm = [regex]::Match($text, '\A---\n[\s\S]*?\n---(?:\n|\z)')
    if ($fm.Success) { Clear-Span $chars 0 $fm.Length }
    $offset = 0; $fence = $null
    foreach ($line in $text.Split("`n")) {
        $m = [regex]::Match($line, '^[ ]{0,3}(`{3,}|~{3,})')
        if ($fence) {
            Clear-Span $chars $offset $line.Length
            if ($m.Success -and $m.Groups[1].Value.StartsWith($fence)) { $fence = $null }
        } elseif ($m.Success) { $fence = $m.Groups[1].Value; Clear-Span $chars $offset $line.Length }
        $offset += $line.Length + 1
    }
    $work = [string]::new($chars)
    foreach ($m in [regex]::Matches($work, '<!--[\s\S]*?-->')) { Clear-Span $chars $m.Index $m.Length }
    $work = [string]::new($chars)
    foreach ($m in [regex]::Matches($work, '(?s)(`+)(.+?)(?<!`)\1(?!`)')) {
        if ($m.Value -notmatch '\n[ \t]*\n') { Clear-Span $chars $m.Index $m.Length }
    }
    $work = [string]::new($chars)
    foreach ($m in [regex]::Matches($work, '&#?\w+;')) { Clear-Span $chars $m.Index $m.Length }
    return , $chars
}

# Blocks: paragraphs, list items, and one-line units for headings and table rows. Returns @(start, end) pairs.
function Get-Blocks([string] $masked) {
    $blocks = [Collections.Generic.List[int[]]]::new()
    $cur = $null; $offset = 0
    foreach ($line in $masked.Split("`n")) {
        $end = $offset + $line.Length
        if ($line -match '^\s*$' -or $line -match '^\s*([-*_])(\s*\1){2,}\s*$') {
            if ($cur) { $blocks.Add($cur); $cur = $null }
        } elseif ($line -match '^\s{0,3}#{1,6}\s' -or $line -match '^\s*\|') {
            if ($cur) { $blocks.Add($cur); $cur = $null }
            $blocks.Add(@($offset, $end))
        } elseif ($line -match '^\s*(?:>\s*)*(?:[-*+]|\d+[.)])\s+') {
            if ($cur) { $blocks.Add($cur) }
            $cur = @($offset, $end)
        } elseif ($cur) { $cur[1] = $end } else { $cur = @($offset, $end) }
        $offset = $end + 1
    }
    if ($cur) { $blocks.Add($cur) }
    return , $blocks
}

function Resolve-LinkTarget([string] $target, [string] $articleDir) {
    $t = ($target -split '[#?]', 2)[0]
    if (-not $t) { return @{ Kind = 'anchor' } }
    $t = [Uri]::UnescapeDataString($t)
    $candidates = if ($t.StartsWith('/')) { @(Join-Path $root $t.TrimStart('/')) } else { @((Join-Path $articleDir $t), (Join-Path $root $t)) }
    $inside = $false
    foreach ($c in $candidates) {
        $full = [IO.Path]::GetFullPath($c)
        if (-not ($full + [IO.Path]::DirectorySeparatorChar).StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and $full -ne $root) { continue }
        $inside = $true
        if (Test-Path -LiteralPath $full) { return @{ Kind = 'file'; Path = $full } }
    }
    if (-not $inside) { return @{ Kind = 'outside' } }
    return @{ Kind = 'dead' }
}

$fileValues = @{}
function Get-FileValues([string] $full) {
    if (-not $fileValues.ContainsKey($full)) {
        $text = [IO.File]::ReadAllText($full)
        $set = [Collections.Generic.HashSet[double]]::new()
        foreach ($m in $FileNumberRx.Matches($text)) {
            $v = 0.0
            if ([double]::TryParse($m.Value.Replace(',', ''), [Globalization.NumberStyles]::Float, $inv, [ref] $v)) { [void] $set.Add([math]::Abs($v)) }
        }
        $fileValues[$full] = [double[]] @($set)
    }
    return , $fileValues[$full]
}

function Test-Number($tok, [double[]] $values) {
    $tol = 0.5 * [math]::Pow(10, - $tok.Decimals) + 1e-9
    $x = $tok.Value
    foreach ($v in $values) {
        if ([math]::Abs($v - $x) -le $tol) { return $true }
        if ($tok.Percent -and [math]::Abs($v * 100 - $x) -le $tol) { return $true }
        if ($tok.Scale -gt 1 -and [math]::Abs($v / $tok.Scale - $x) -le $tol) { return $true }
    }
    return $false
}

# ---------- one file ----------

function Test-Article([string] $full) {
    $problems = [Collections.Generic.List[object]]::new()
    $notes = [Collections.Generic.List[object]]::new()
    $external = [Collections.Generic.List[object]]::new()
    $shown = Get-DisplayPath $full
    $dir = Split-Path $full -Parent
    $text = [IO.File]::ReadAllText($full).Replace("`r`n", "`n").Replace("`r", "`n")
    $starts = [Collections.Generic.List[int]]::new(); $starts.Add(0)
    for ($i = 0; $i -lt $text.Length; $i++) { if ($text[$i] -eq "`n") { $starts.Add($i + 1) } }
    $starts = $starts.ToArray()
    $add = { param($list, $offset, $msg) $list.Add([pscustomobject]@{ File = $shown; Line = (Get-LineNumber $starts $offset); Message = $msg }) }

    # 3. Banned terms, anywhere.
    if ($Banned.Count) {
        $rx = [regex] ('(?i)(?<!\w)(?:' + (($Banned | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')(?!\w)')
        foreach ($m in $rx.Matches($text)) { & $add $problems $m.Index "banned term '$($m.Value)'" }
    }

    $maskedChars = Get-MaskedText $text
    $masked = [string]::new($maskedChars)
    $styleChars = $maskedChars.Clone()

    # Reference definitions: check the target, then drop the line from prose.
    $refs = @{}
    foreach ($m in $RefDefRx.Matches($masked)) {
        $refs[$m.Groups['ref'].Value.ToLowerInvariant()] = @{ Target = $m.Groups['target'].Value; Offset = $m.Index }
        Clear-Span $maskedChars $m.Index $m.Length; Clear-Span $styleChars $m.Index $m.Length
    }
    $masked = [string]::new($maskedChars)
    # Prose view: block markers (#, >, list bullets, table pipes) blanked so they aren't read as numbers.
    $proseChars = $masked.ToCharArray()
    foreach ($m in [regex]::Matches($masked, '(?m)^\s{0,3}#{1,6}\s|^\s*(?:>\s*)+|^\s*(?:[-*+]|\d+[.)])\s+|\|')) { Clear-Span $proseChars $m.Index $m.Length }
    $prose = [string]::new($proseChars)

    $checked = @{}
    $checkLink = {
        param($target, $offset)
        if ($target -match '^[a-zA-Z][\w+.-]*:') {
            if ($target -match '^https?:') { & $add $external $offset $target }
            return $null
        }
        $r = Resolve-LinkTarget $target $dir
        if (-not $checked.ContainsKey($offset)) {
            $checked[$offset] = $true
            if ($r.Kind -eq 'dead') { & $add $problems $offset "dead link '$target'" }
            elseif ($r.Kind -eq 'outside') { & $add $problems $offset "link '$target' points outside the repository" }
        }
        if ($r.Kind -eq 'file') { return $r.Path } else { return $null }
    }
    foreach ($ref in $refs.Values) { [void] (& $checkLink $ref.Target $ref.Offset) }

    foreach ($b in (Get-Blocks $masked)) {
        $bs = $b[0]; $len = $b[1] - $b[0]
        if ($len -le 0) { continue }
        $view = $prose.Substring($bs, $len).Replace("`n", ' ')
        $numChars = $view.ToCharArray(); $splitChars = $view.ToCharArray()
        $links = [Collections.Generic.List[object]]::new()
        foreach ($m in @($LinkRx.Matches($view)) + @($RefLinkRx.Matches($view))) {
            if ($m.Groups['target'].Success -and $m.Groups['target'].Value) { $target = $m.Groups['target'].Value }
            else {
                $key = if ($m.Groups['ref'].Value) { $m.Groups['ref'].Value } else { $m.Groups['text'].Value }
                $def = $refs[$key.ToLowerInvariant()]
                if (-not $def) { continue }
                $target = $def.Target
            }
            if (@($links | Where-Object { $m.Index -ge $_.Start -and $m.Index -lt $_.End }).Count) { continue }
            $resolved = & $checkLink $target ($bs + $m.Index)
            $textEnd = $m.Groups['text'].Index + $m.Groups['text'].Length
            Clear-Span $numChars $textEnd ($m.Index + $m.Length - $textEnd)
            Clear-Span $styleChars ($bs + $textEnd) ($m.Index + $m.Length - $textEnd)
            if ($m.Groups['text'].Value -match '[/\\]|\.\w{2,5}$') { Clear-Span $numChars $m.Groups['text'].Index $m.Groups['text'].Length }
            for ($i = $m.Index; $i -lt $m.Index + $m.Length; $i++) { if ('.!?'.Contains($splitChars[$i])) { $splitChars[$i] = '_' } }
            $isReport = $resolved -and (Test-Path -LiteralPath $resolved -PathType Leaf) -and ($ReportExt -contains [IO.Path]::GetExtension($resolved).ToLowerInvariant())
            $links.Add([pscustomobject]@{ Start = $m.Index; End = $m.Index + $m.Length; Report = $(if ($isReport) { $resolved } else { $null }) })
        }
        foreach ($m in $UrlRx.Matches($view)) {
            if (-not @($links | Where-Object { $m.Index -ge $_.Start -and $m.Index -lt $_.End }).Count) { & $add $external ($bs + $m.Index) $m.Value }
            Clear-Span $numChars $m.Index $m.Length; Clear-Span $styleChars ($bs + $m.Index) $m.Length
            for ($i = $m.Index; $i -lt $m.Index + $m.Length; $i++) { if ('.!?'.Contains($splitChars[$i])) { $splitChars[$i] = '_' } }
        }
        foreach ($ex in $Exemptions) {
            foreach ($m in [regex]::Matches([string]::new($numChars), $ex.Pattern)) { Clear-Span $numChars $m.Index $m.Length }
        }

        # Sentences.
        $split = [string]::new($splitChars)
        $cuts = [Collections.Generic.List[int]]::new(); $cuts.Add(0)
        foreach ($m in [regex]::Matches($split, '[.!?]+["'')\]]*(?=\s|$)')) {
            if ($m.Value.StartsWith('.')) {
                $w = [regex]::Match($split.Substring(0, $m.Index), '([A-Za-z.]+)$')
                if ($w.Success -and $Abbreviations -contains $w.Groups[1].Value.ToLowerInvariant().TrimStart('.')) { continue }
            }
            $cuts.Add($m.Index + $m.Length)
        }
        $cuts.Add($view.Length)
        $numbers = @(foreach ($m in $NumberRx.Matches([string]::new($numChars))) {
            $raw = $m.Groups['num'].Value
            $dot = $raw.IndexOf('.')
            $scale = switch ($m.Groups['mul'].Value) { 'M' { 1e6 } 'k' { 1e3 } 'B' { 1e9 } 'bn' { 1e9 } default { 1 } }
            [pscustomobject]@{
                Index = $m.Index; Text = $m.Value.Trim(); Decimals = $(if ($dot -ge 0) { $raw.Length - $dot - 1 } else { 0 })
                Value = [double]::Parse($raw.Replace(',', ''), $inv); Percent = $m.Groups['pct'].Success; Scale = $scale
            }
        })
        for ($s = 0; $s -lt $cuts.Count - 1; $s++) {
            $a = $cuts[$s]; $z = $cuts[$s + 1]
            $inSentence = @($numbers | Where-Object { $_.Index -ge $a -and $_.Index -lt $z })
            if (-not $inSentence.Count) { continue }
            $reports = @($links | Where-Object { $_.Start -ge $a -and $_.Start -lt $z -and $_.Report } | ForEach-Object Report | Select-Object -Unique)
            if (-not $reports.Count) {
                & $add $notes ($bs + $inSentence[0].Index) ("unlinked numbers: " + (($inSentence | ForEach-Object Text) -join ', '))
                continue
            }
            $values = [double[]] @($reports | ForEach-Object { Get-FileValues $_ } | ForEach-Object { $_ })
            foreach ($n in $inSentence) {
                if (-not (Test-Number $n $values)) {
                    & $add $problems ($bs + $n.Index) ("number $($n.Text) not found in " + (($reports | ForEach-Object { Get-DisplayPath $_ }) -join ', '))
                }
            }
        }
    }

    # 4. Prose style, outside code and link targets.
    $style = [string]::new($styleChars)
    foreach ($m in [regex]::Matches($style, '—')) { & $add $problems $m.Index 'em dash in prose' }
    foreach ($m in [regex]::Matches($style, '(?<=\s)–(?=\s)')) { & $add $problems $m.Index 'en dash used as a dash' }
    foreach ($m in [regex]::Matches($style, ';')) { & $add $problems $m.Index 'semicolon in prose' }

    return [pscustomobject]@{ Problems = $problems; Notes = $notes; External = $external }
}

# ---------- main ----------

$Banned = Split-List $Banned
$targets = if ($Files) {
    foreach ($f in (Split-List $Files)) {
        $p = if ([IO.Path]::IsPathRooted($f) -or (Test-Path -LiteralPath $f)) { $f } else { Join-Path $root $f }
        if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { throw "no such file: $f" }
        (Resolve-Path -LiteralPath $p).Path
    }
} else {
    $dirPath = if ([IO.Path]::IsPathRooted($Path) -or (Test-Path -LiteralPath $Path)) { $Path } else { Join-Path $root $Path }
    if (-not (Test-Path -LiteralPath $dirPath -PathType Container)) { throw "no such folder: $Path" }
    Get-ChildItem -LiteralPath $dirPath -Filter *.md -File | Sort-Object Name | ForEach-Object FullName
}
$targets = @($targets)
if (-not $targets.Count) { throw "no Markdown files to check" }

$allProblems = [Collections.Generic.List[object]]::new()
$allNotes = [Collections.Generic.List[object]]::new()
$allExternal = [Collections.Generic.List[object]]::new()
foreach ($t in $targets) {
    $r = Test-Article $t
    $allProblems.AddRange($r.Problems); $allNotes.AddRange($r.Notes); $allExternal.AddRange($r.External)
}

foreach ($p in ($allProblems | Sort-Object File, Line)) { Write-Output "$($p.File):$($p.Line): $($p.Message)" }
if ($allExternal.Count) {
    Write-Host "External links, not checked ($($allExternal.Count)):"
    foreach ($e in $allExternal) { Write-Host "  $($e.File):$($e.Line)  $($e.Message)" }
}
if ($allNotes.Count) {
    Write-Verbose "Sentences with unlinked numbers ($($allNotes.Count)), for the author to review:"
    foreach ($n in $allNotes) { Write-Verbose "  $($n.File):$($n.Line): $($n.Message)" }
}
Write-Host ("check-articles: {0} file(s), {1} problem(s)" -f $targets.Count, $allProblems.Count)
exit $(if ($allProblems.Count) { 1 } else { 0 })
