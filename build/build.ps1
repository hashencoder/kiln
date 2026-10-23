<#
.SYNOPSIS
  Builds WebHut.exe and whut.exe on Windows with the C# compiler that ships
  with the .NET Framework 4.8 (no Visual Studio or SDK needed).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File build\build.ps1
  powershell -ExecutionPolicy Bypass -File build\build.ps1 -Package -Version 1.0.0
#>
param(
    [switch]$Package,          # also create dist\WebHut-<version>-win64.zip
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
          "System.Net.Http.dll", "System.Security.dll") | ForEach-Object { "/reference:`"$(Join-Path $fw $_)`"" }

$common = @("/nologo", "/optimize+", "/debug-", "/platform:anycpu", "/warn:4", "/win32icon:`"$(Join-Path $repo 'assets\webhut.ico')`"") + $refs + $resources
$core = Get-ChildItem (Join-Path $repo "src\Core\*.cs") | ForEach-Object { "`"$($_.FullName)`"" }
$gui  = Get-ChildItem (Join-Path $repo "src\Gui\*.cs")  | ForEach-Object { "`"$($_.FullName)`"" }
$cli  = Get-ChildItem (Join-Path $repo "src\Cli\*.cs")  | ForEach-Object { "`"$($_.FullName)`"" }

function Invoke-Csc([string[]]$csArgs) {
    $rsp = Join-Path $env:TEMP ("webhut-build-" + [guid]::NewGuid() + ".rsp")
    [System.IO.File]::WriteAllLines($rsp, [string[]]$csArgs)  # UTF-8 without BOM
    try {
        & $csc "@$rsp"
        if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }
    } finally { Remove-Item $rsp -ErrorAction SilentlyContinue }
}

Write-Host "Building WebHut.exe..."
# Fonts (Inter, JetBrains Mono; SIL OFL 1.1) are embedded in the GUI only, as fonts/<file>
$fonts = Get-ChildItem -Path (Join-Path $repo "assets\fonts\*.ttf") | Sort-Object Name | ForEach-Object { "/resource:`"$($_.FullName)`",fonts/$($_.Name)" }
# Logo renders (build/make_icon.py), drawn by Ui.DrawLogo
$fonts += Get-ChildItem -Path (Join-Path $repo "assets\logo\webhut-*.png") | Where-Object { $_.Name -match '^webhut-\d+\.png$' } | Sort-Object Name | ForEach-Object { "/resource:`"$($_.FullName)`",logo/$($_.Name)" }
$fonts += "/resource:`"$(Join-Path $repo 'assets\webhut.ico')`",logo/webhut.ico"
Invoke-Csc ($common + @("/target:winexe", "/out:`"$out\WebHut.exe`"", "/main:WebHut.Program") + $fonts + $core + $gui)
Write-Host "Building whut.exe..."
Invoke-Csc ($common + @("/target:exe", "/out:`"$out\whut.exe`"", "/main:WebHut.CliProgram") + $core + $cli)

Copy-Item (Join-Path $repo "build\WebHut.exe.config") (Join-Path $out "WebHut.exe.config") -Force
Copy-Item (Join-Path $repo "build\WebHut.exe.config") (Join-Path $out "whut.exe.config") -Force

if ($Package) {
    $stage = Join-Path $out "WebHut"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item "$out\WebHut.exe", "$out\WebHut.exe.config", "$out\whut.exe", "$out\whut.exe.config" $stage
    Copy-Item (Join-Path $repo "LICENSE") $stage
    Copy-Item (Join-Path $repo "dist-readme.txt") (Join-Path $stage "README.txt")
    $lic = Join-Path $stage "licenses"
    New-Item -ItemType Directory -Path $lic | Out-Null
    Copy-Item (Join-Path $repo "assets\fonts\OFL-*.txt") $lic
    $zip = Join-Path $out "WebHut-$Version-win64.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path $stage -DestinationPath $zip
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
    Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ASCII
    Write-Host "Packaged $zip"
    Write-Host "SHA-256  $hash"

    # Installer (Inno Setup 6), when ISCC.exe is available
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
              "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($iscc) {
        $setupVersion = ($Version -replace '[^0-9.].*$', '')
        if (-not $setupVersion) { $setupVersion = "0.0.0" }
        & $iscc "/DAppVersion=$setupVersion" "/DStage=$stage" "/DOutDir=$out" (Join-Path $repo "build\installer.iss")
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
        $setup = Join-Path $out "WebHut-Setup-$setupVersion.exe"
        $shash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLower()
        Set-Content -Path "$setup.sha256" -Value "$shash  $(Split-Path $setup -Leaf)" -Encoding ASCII
        Write-Host "Installer $setup"
        Write-Host "SHA-256  $shash"
    } else {
        Write-Host "Inno Setup 6 not found: skipping the installer (zip only)."
    }
}
Write-Host "Done: $out"
