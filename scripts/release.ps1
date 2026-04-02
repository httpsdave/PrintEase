param(
    [Parameter(Mandatory = $true)]
    [string]$Message,

    [string]$Version = "1.0.4",

    [switch]$PushTag
)

$ErrorActionPreference = "Stop"

Write-Host "Preparing release commit on current branch..."

git reset

git add .

$status = git status --short
if (-not $status) {
    Write-Host "No changes to commit."
    exit 0
}

git commit -m $Message

git push

if ($PushTag) {
    $tag = "v$Version"
    $existingTag = git tag --list $tag
    if ($existingTag) {
        throw "Tag $tag already exists. Choose a different -Version."
    }

    git tag $tag
    git push origin $tag
    Write-Host "Pushed tag $tag"
}

Write-Host "Release automation complete."
