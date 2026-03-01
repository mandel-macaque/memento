Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-NativeAotOutput {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)]
    [string]$OutputDirectory,

    [Parameter(Mandatory)]
    [string]$ExecutableName
  )

  if (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container)) {
    throw "Missing output directory ${OutputDirectory}."
  }

  $dlls = @(Get-ChildItem -Path $OutputDirectory -Filter '*.dll' -File -ErrorAction SilentlyContinue)
  if ($dlls.Count -gt 0) {
    throw 'Publish output contains .dll files, expected NativeAOT single-binary output.'
  }

  $exePath = Join-Path -Path $OutputDirectory -ChildPath $ExecutableName
  if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Missing expected executable ${exePath}."
  }
}

function New-ReleaseArchive {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)]
    [ValidateSet('tar.gz', 'zip')]
    [string]$ArchiveType,

    [Parameter(Mandatory)]
    [string]$Rid,

    [Parameter(Mandatory)]
    [string]$OutputDirectory
  )

  $binaryName = if ($ArchiveType -eq 'zip') { 'git-memento.exe' } else { 'git-memento' }
  $binaryPath = Join-Path -Path $OutputDirectory -ChildPath $binaryName
  if (-not (Test-Path -LiteralPath $binaryPath -PathType Leaf)) {
    throw "Missing expected executable ${binaryPath}."
  }

  if ($ArchiveType -eq 'zip') {
    $archivePath = "git-memento-${Rid}.zip"
    if (Test-Path -LiteralPath $archivePath) {
      Remove-Item -LiteralPath $archivePath -Force
    }

    Compress-Archive -Path $binaryPath -DestinationPath $archivePath
    return $archivePath
  }

  $archivePath = "git-memento-${Rid}.tar.gz"
  if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
  }

  $workingDirectory = (Get-Location).Path
  try {
    Set-Location -LiteralPath $OutputDirectory
    tar -czf (Join-Path -Path $workingDirectory -ChildPath $archivePath) $binaryName
    if ($LASTEXITCODE -ne 0) {
      throw "Failed to create ${archivePath}."
    }
  }
  finally {
    Set-Location -LiteralPath $workingDirectory
  }

  return $archivePath
}

function Get-ZipEntries {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)]
    [string]$ArchivePath
  )

  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $zip = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
  try {
    return @($zip.Entries | ForEach-Object { $_.FullName })
  }
  finally {
    $zip.Dispose()
  }
}

function Get-TarEntries {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)]
    [string]$ArchivePath
  )

  $entries = tar -tzf $ArchivePath
  if ($LASTEXITCODE -ne 0) {
    throw "Failed to inspect archive ${ArchivePath}."
  }

  return @($entries | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Test-ArchiveSingleEntry {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)]
    [ValidateSet('tar.gz', 'zip')]
    [string]$ArchiveType,

    [Parameter(Mandatory)]
    [string]$ArchivePath,

    [Parameter(Mandatory)]
    [string]$ExpectedEntry
  )

  if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) {
    throw "Missing archive ${ArchivePath}."
  }

  $resolvedArchivePath = (Resolve-Path -LiteralPath $ArchivePath).Path
  $entries = @(
    if ($ArchiveType -eq 'zip') {
    Get-ZipEntries -ArchivePath $resolvedArchivePath
  }
  else {
    Get-TarEntries -ArchivePath $resolvedArchivePath
  }
  )

  if ($entries.Count -ne 1 -or $entries[0] -ne $ExpectedEntry) {
    $names = $entries -join ', '
    throw "Unexpected archive contents for ${ArchivePath}: ${names}"
  }
}

Export-ModuleMember -Function Test-NativeAotOutput, New-ReleaseArchive, Test-ArchiveSingleEntry
