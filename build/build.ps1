<#
.SYNOPSIS
  Builds Kiln.exe and kilnctl.exe on Windows with the C# compiler that ships
  with the .NET Framework 4.8 (no Visual Studio or SDK needed).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File build\build.ps1
  powershell -ExecutionPolicy Bypass -File build\build.ps1 -Package -Version 1.0.0
#>
param(
    [switch]$Package,          # also create dist\Kiln-<version>-win64.zip
    [string]$Version = "1.0.0"
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$fw  = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$csc = Join-Path $fw "csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc. Is .NET Framework 4.8 installed?" }

$out = Join-Path $repo "dist"
New-Item -ItemType Directory -Force -Path $out | Out-Null

# Embed every file under overlay\ as resource "overlay/<relative/path>"
$resources = Get-ChildItem -Path (Join-Path $repo "overlay") -Recurse -File | Sort-Object FullName | ForEach-Object {
    $rel = $_.FullName.Substring($repo.Length + 1).Replace('\', '/')
    "/resource:`"$($_.FullName)`",$rel"
}

$refs = @("System.dll", "System.Core.dll", "System.Drawing.dll", "System.Windows.Forms.dll",
          "System.Web.Extensions.dll", "System.IO.Compression.dll", "System.IO.Compression.FileSystem.dll",
          "System.Net.Http.dll") | ForEach-Object { "/reference:`"$(Join-Path $fw $_)`"" }

$common = @("/nologo", "/optimize+", "/debug-", "/platform:anycpu", "/warn:4", "/win32icon:`"$(Join-Path $repo 'assets\kiln.ico')`"") + $refs + $resources
$core = Get-ChildItem (Join-Path $repo "src\Core\*.cs") | ForEach-Object { "`"$($_.FullName)`"" }
$gui  = Get-ChildItem (Join-Path $repo "src\Gui\*.cs")  | ForEach-Object { "`"$($_.FullName)`"" }
$cli  = Get-ChildItem (Join-Path $repo "src\Cli\*.cs")  | ForEach-Object { "`"$($_.FullName)`"" }

function Invoke-Csc([string[]]$csArgs) {
    $rsp = Join-Path $env:TEMP ("kiln-build-" + [guid]::NewGuid() + ".rsp")
    [System.IO.File]::WriteAllLines($rsp, [string[]]$csArgs)  # UTF-8 without BOM
    try {
        & $csc "@$rsp"
        if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }
    } finally { Remove-Item $rsp -ErrorAction SilentlyContinue }
}

Write-Host "Building Kiln.exe..."
Invoke-Csc ($common + @("/target:winexe", "/out:`"$out\Kiln.exe`"", "/main:Kiln.Program") + $core + $gui)
Write-Host "Building kilnctl.exe..."
Invoke-Csc ($common + @("/target:exe", "/out:`"$out\kilnctl.exe`"", "/main:Kiln.CliProgram") + $core + $cli)

Copy-Item (Join-Path $repo "build\Kiln.exe.config") (Join-Path $out "Kiln.exe.config") -Force
Copy-Item (Join-Path $repo "build\Kiln.exe.config") (Join-Path $out "kilnctl.exe.config") -Force

if ($Package) {
    $stage = Join-Path $out "Kiln"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item "$out\Kiln.exe", "$out\Kiln.exe.config", "$out\kilnctl.exe", "$out\kilnctl.exe.config" $stage
    Copy-Item (Join-Path $repo "LICENSE") $stage
    Copy-Item (Join-Path $repo "dist-readme.txt") (Join-Path $stage "README.txt")
    $zip = Join-Path $out "Kiln-$Version-win64.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path $stage -DestinationPath $zip
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
    Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ASCII
    Write-Host "Packaged $zip"
    Write-Host "SHA-256  $hash"
}
Write-Host "Done: $out"
