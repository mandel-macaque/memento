#!/usr/bin/env pwsh
param(
  [Parameter(Mandatory)]
  [string]$Rid,

  [Parameter(Mandatory)]
  [ValidateSet('tar.gz', 'zip')]
  [string]$ArchiveType
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'ReleaseArtifacts.psm1') -Force

$archivePath = if ($ArchiveType -eq 'zip') {
  "git-memento-${Rid}.zip"
}
else {
  "git-memento-${Rid}.tar.gz"
}

$expectedEntry = if ($ArchiveType -eq 'zip') { 'git-memento.exe' } else { 'git-memento' }

Test-ArchiveSingleEntry -ArchiveType $ArchiveType -ArchivePath $archivePath -ExpectedEntry $expectedEntry
