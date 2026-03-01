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

$outDir = "out/${Rid}"
$executableName = if ($ArchiveType -eq 'zip') { 'git-memento.exe' } else { 'git-memento' }

Test-NativeAotOutput -OutputDirectory $outDir -ExecutableName $executableName
