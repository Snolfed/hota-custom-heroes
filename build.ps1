# Builds the mod and packs a release zip into dist\.
# Needs Visual Studio 2022 (or Build Tools) with the C++ x86 tools, and .NET Framework 4 (part of Windows).
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "dist\build"
$pkg = Join-Path $root "dist\package"
Remove-Item -Recurse -Force (Join-Path $root "dist") -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out, "$pkg\WlasniBohaterowie" | Out-Null

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "Visual Studio C++ tools not found" }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars32.bat"

function Invoke-Cl($dir, $clArgs) {
    cmd /c "`"$vcvars`" >nul 2>nul && cd /d `"$dir`" && cl /nologo /O2 /W3 /LD /MT /D_CRT_SECURE_NO_WARNINGS $clArgs"
    if ($LASTEXITCODE -ne 0) { throw "cl failed in $dir" }
}

Copy-Item "$root\src\WlasniBohaterowie\*", "$root\src\proxy\*" $out
Invoke-Cl $out "WlasniBohaterowie.cpp /Fe:WlasniBohaterowie.dll user32.lib kernel32.lib"
Invoke-Cl $out "smackw32_proxy.cpp /Fe:SMACKW32.DLL kernel32.lib"

$csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
& $csc /nologo /codepage:65001 /target:winexe /out:"$out\Kreator.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll "$root\src\Kreator\Kreator.cs"
if ($LASTEXITCODE -ne 0) { throw "csc failed" }

Copy-Item "$out\SMACKW32.DLL" $pkg
Copy-Item "$out\WlasniBohaterowie.dll", "$out\Kreator.exe", "$root\examples\bohaterowie.ini" "$pkg\WlasniBohaterowie"
Copy-Item "$root\INSTALL.txt", "$root\LICENSE" $pkg
$zip = Join-Path $root "dist\hota-custom-heroes-$Version.zip"
Compress-Archive -Path "$pkg\*" -DestinationPath $zip
Write-Host "Built $zip"
