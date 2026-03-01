#!/usr/bin/env pwsh
param(
  [Parameter(Mandatory)]
  [string]$TargetSha,

  [Parameter()]
  [string]$HeadBranch,

  [Parameter(Mandatory)]
  [string]$ProjectPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$shortSha = $TargetSha.Substring(0, [Math]::Min(8, $TargetSha.Length))
$releaseTag = ''
$releaseName = ''

$currentTag = ''
if ($HeadBranch -ne 'main') {
  $currentTag = git tag --points-at $TargetSha | Where-Object { $_ -match '^v' } | Select-Object -First 1
}

if (-not [string]::IsNullOrWhiteSpace($currentTag)) {
  $releaseTag = $currentTag
  $releaseName = $currentTag
}
else {
  $versionLines = dotnet msbuild $ProjectPath -nologo -getProperty:Version
  if ($LASTEXITCODE -ne 0) {
    throw 'Failed to resolve Version from project file.'
  }

  $version = ($versionLines | Select-Object -Last 1).Trim()
  if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'Failed to resolve Version from project file.'
  }

  $releaseTag = "${version}-${shortSha}"
  $releaseName = "Build ${releaseTag}"
}

if ([string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
  throw 'GITHUB_OUTPUT is not set.'
}

Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "target_sha=${TargetSha}"
Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "release_tag=${releaseTag}"
Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "release_name=${releaseName}"
