<#
.SYNOPSIS
    Wraps web/slot-demo.html in a document skeleton and writes docs/index.html.

.DESCRIPTION
    The source is a fragment so it can be previewed on its own; the published page needs
    a doctype, a charset and a viewport. The split is at </style>: everything above it
    belongs in the head, everything below it in the body.
#>
param(
    [string] $Source = 'web/slot-demo.html',
    [string] $Out    = 'docs/index.html'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root   = Split-Path -Parent $PSScriptRoot
$lines  = [IO.File]::ReadAllLines((Join-Path $root $Source))

$split = [Array]::FindIndex($lines, [Predicate[string]] { param($l) $l -eq '</style>' })
if ($split -lt 0) { throw "No </style> line in $Source, so the head cannot be found." }

$page = New-Object Collections.Generic.List[string]
$page.AddRange([string[]] @(
    '<!DOCTYPE html>'
    '<html lang="en">'
    '<head>'
    '<meta charset="utf-8">'
    '<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">'
))

# Line 0 is the editor-preview hint the published page replaces with a real viewport tag.
$page.AddRange([string[]] $lines[1..$split])
$page.Add('</head>')
$page.Add('<body>')
$page.AddRange([string[]] $lines[($split + 1)..($lines.Length - 1)])
$page.Add('</body>')
$page.Add('</html>')

$target = Join-Path $root $Out
[IO.File]::WriteAllLines($target, $page, (New-Object Text.UTF8Encoding($false)))

"Wrote $target ($($page.Count) lines)."
