Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot '../../scripts/release/ReleaseArtifacts.psm1') -Force

Describe 'Test-ArchiveSingleEntry' {
  It 'accepts a zip archive with exactly git-memento.exe' {
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ([System.IO.Path]::GetRandomFileName())
    New-Item -ItemType Directory -Path $temp | Out-Null
    try {
      $binary = Join-Path $temp 'git-memento.exe'
      'binary' | Set-Content -Path $binary -NoNewline
      $zipPath = Join-Path $temp 'artifact.zip'
      Compress-Archive -Path $binary -DestinationPath $zipPath

      { Test-ArchiveSingleEntry -ArchiveType 'zip' -ArchivePath $zipPath -ExpectedEntry 'git-memento.exe' } | Should -Not -Throw
    }
    finally {
      Remove-Item -LiteralPath $temp -Recurse -Force
    }
  }

  It 'fails when zip archive contains additional files' {
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ([System.IO.Path]::GetRandomFileName())
    New-Item -ItemType Directory -Path $temp | Out-Null
    try {
      $binary = Join-Path $temp 'git-memento.exe'
      $note = Join-Path $temp 'README.txt'
      'binary' | Set-Content -Path $binary -NoNewline
      'extra' | Set-Content -Path $note -NoNewline
      $zipPath = Join-Path $temp 'artifact.zip'
      Compress-Archive -Path @($binary, $note) -DestinationPath $zipPath

      {
        Test-ArchiveSingleEntry -ArchiveType 'zip' -ArchivePath $zipPath -ExpectedEntry 'git-memento.exe'
      } | Should -Throw '*Unexpected archive contents*'
    }
    finally {
      Remove-Item -LiteralPath $temp -Recurse -Force
    }
  }
}

Describe 'Test-NativeAotOutput' {
  It 'fails when dll files are present' {
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ([System.IO.Path]::GetRandomFileName())
    New-Item -ItemType Directory -Path $temp | Out-Null
    try {
      New-Item -ItemType File -Path (Join-Path $temp 'git-memento.exe') | Out-Null
      New-Item -ItemType File -Path (Join-Path $temp 'dependency.dll') | Out-Null

      {
        Test-NativeAotOutput -OutputDirectory $temp -ExecutableName 'git-memento.exe'
      } | Should -Throw '*contains .dll files*'
    }
    finally {
      Remove-Item -LiteralPath $temp -Recurse -Force
    }
  }

  It 'fails when expected executable is missing' {
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ([System.IO.Path]::GetRandomFileName())
    New-Item -ItemType Directory -Path $temp | Out-Null
    try {
      {
        Test-NativeAotOutput -OutputDirectory $temp -ExecutableName 'git-memento.exe'
      } | Should -Throw '*Missing expected executable*'
    }
    finally {
      Remove-Item -LiteralPath $temp -Recurse -Force
    }
  }
}
