# Fact Vault Manager - Create Template Folders
# Run from the root of your FactVaultManager project

$templates = @(
    "Standard Fact",
    "Animal Fact",
    "History Fact",
    "Science Fact",
    "Space Fact",
    "Haunted Fact"
)

$root = Join-Path $PSScriptRoot "templates"

New-Item -ItemType Directory -Force -Path $root | Out-Null

foreach ($template in $templates) {

    $folder = Join-Path $root $template
    New-Item -ItemType Directory -Force -Path $folder | Out-Null

    @"
HOOK

INTRO

FACT 1

FACT 2

FACT 3

OUTRO
"@ | Set-Content (Join-Path $folder "Script.txt") -Encoding UTF8

    @"
Write your YouTube description here...

#facts #shorts
"@ | Set-Content (Join-Path $folder "Description.txt") -Encoding UTF8

    @"
Thumbnail Ideas:

Research:

Voice-over Notes:

Upload Checklist:
"@ | Set-Content (Join-Path $folder "Notes.txt") -Encoding UTF8

    @"
Thanks for watching!

What fact should we cover next?
"@ | Set-Content (Join-Path $folder "Pinned Comment.txt") -Encoding UTF8
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Green
Write-Host " Fact Vault Manager Templates Created!" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Location: $root"
