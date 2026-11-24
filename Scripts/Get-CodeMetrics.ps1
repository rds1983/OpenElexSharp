#!/usr/bin/env pwsh
#Requires -Version 5.1
<#
.SYNOPSIS
    Counts C# source files, lines, non-blank LOC and total size for README metrics.
.DESCRIPTION
    Scans OpenElexSharp/ and OpenElexSharp.Tests/, excluding bin/ and obj/.
    Prints a table that can be copied into README.md.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$scopes = @(
    @{ Name = 'Engine'; Path = Join-Path $root 'OpenElexSharp' }
    @{ Name = 'Tests';  Path = Join-Path $root 'OpenElexSharp.Tests' }
)

$totalFiles = 0
$totalLines = 0
$totalCode = 0
$totalBytes = [long]0
$rows = @()

foreach ($scope in $scopes) {
    $files = @()
    if (Test-Path -LiteralPath $scope.Path) {
        $files = @(Get-ChildItem -LiteralPath $scope.Path -Recurse -File -Filter '*.cs' |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
    }

    $lines = 0
    $code = 0
    $bytes = [long]0

    foreach ($file in $files) {
        $content = @(Get-Content -LiteralPath $file.FullName)
        $lines += $content.Count
        $code += @($content | Where-Object { $_.Trim().Length -gt 0 }).Count
        $bytes += $file.Length
    }

    $totalFiles += $files.Count
    $totalLines += $lines
    $totalCode += $code
    $totalBytes += $bytes

    $rows += [pscustomobject]@{
        Scope         = $scope.Name
        Files         = $files.Count
        Lines         = $lines
        'Non-blank'   = $code
        Size          = '{0:N1} KB' -f ($bytes / 1KB)
    }
}

$rows += [pscustomobject]@{
    Scope       = 'Total'
    Files       = $totalFiles
    Lines       = $totalLines
    'Non-blank' = $totalCode
    Size        = '{0:N1} KB' -f ($totalBytes / 1KB)
}

$rows | Format-Table -AutoSize
