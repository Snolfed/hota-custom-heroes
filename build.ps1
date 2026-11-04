# Builds HeroForge and packs a release zip into dist\.
# Needs Visual Studio 2022 (or Build Tools) with the C++ x86 tools, and .NET Framework 4 (part of Windows).
param([string]$Version = "1.3.0")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "dist\build"
$pkg = Join-Path $root "dist\package"
Remove-Item -Recurse -Force (Join-Path $root "dist") -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out, "$pkg\HeroForge" | Out-Null

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "Visual Studio C++ tools not found" }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars32.bat"

function Invoke-Cl($dir, $clArgs) {
    cmd /c "`"$vcvars`" >nul 2>nul && cd /d `"$dir`" && cl /nologo /O2 /W3 /LD /MT /D_CRT_SECURE_NO_WARNINGS $clArgs"
    if ($LASTEXITCODE -ne 0) { throw "cl failed in $dir" }
}

Copy-Item "$root\src\HeroForge\*", "$root\src\proxy\*" $out
Invoke-Cl $out "HeroForge.cpp /Fe:HeroForge.dll user32.lib kernel32.lib psapi.lib"
Invoke-Cl $out "smackw32_proxy.cpp /Fe:SMACKW32.DLL kernel32.lib"

$csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
& $csc /nologo /codepage:65001 /target:winexe /win32icon:"$root\assets\HeroForge.ico" /out:"$out\HeroForge.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.dll "$root\src\Editor\HeroForgeEditor.cs"
if ($LASTEXITCODE -ne 0) { throw "csc failed" }

Copy-Item "$out\SMACKW32.DLL" $pkg
Copy-Item "$out\HeroForge.dll", "$out\HeroForge.exe", "$root\examples\heroes.ini" "$pkg\HeroForge"
Copy-Item "$root\INSTALL.txt", "$root\LICENSE" $pkg
$zip = Join-Path $root "dist\HeroForge-$Version.zip"
Compress-Archive -Path "$pkg\*" -DestinationPath $zip
Write-Host "Built $zip"