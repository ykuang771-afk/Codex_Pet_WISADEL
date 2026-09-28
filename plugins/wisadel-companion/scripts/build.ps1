$ErrorActionPreference = 'Stop'
$pluginRoot = Split-Path $PSScriptRoot -Parent
$buildOutput = Join-Path $pluginRoot 'bin'
New-Item -ItemType Directory -Force -Path $buildOutput | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$counterSource = Join-Path $PSScriptRoot 'CounterModel.cs'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /main:CompanionProgram "/out:$buildOutput/WisadelCompanion.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/reference:$framework/UIAutomationClient.dll" "/reference:$framework/UIAutomationTypes.dll" "/reference:$framework/WindowsBase.dll" $counterSource (Join-Path $PSScriptRoot 'Companion.cs') (Join-Path $PSScriptRoot 'BadgePresentation.cs') (Join-Path $PSScriptRoot 'QuotaMonitor.cs') (Join-Path $PSScriptRoot 'UsageMonitor.cs') (Join-Path $PSScriptRoot 'Speech.cs')
if ($LASTEXITCODE -ne 0) { throw 'Companion build failed' }
