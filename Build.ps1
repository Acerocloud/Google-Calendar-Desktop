param([string]$OutputName='DeskCalendar.exe',[switch]$VisualQA,[switch]$LoginQA,[string]$ClientConfig=(Join-Path $PSScriptRoot 'source/oauth-client.json'))
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $ClientConfig)) { throw 'Copy source/oauth-client.example.json to source/oauth-client.json and supply your own Google Desktop OAuth client before building.' }
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$frameworkPath = Split-Path -Parent $compilerPath
$references = @('System.dll','System.Core.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Security.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xaml.dll','WPF/WindowsBase.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll') | ForEach-Object { '/r:' + (Join-Path $frameworkPath $_) }
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'source') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$buildDefine = if($LoginQA){'/define:VISUAL_QA;LOGIN_QA'}elseif($VisualQA){'/define:VISUAL_QA'}else{'/define:RELEASE'}
& $compilerPath $buildDefine /nologo /target:winexe /platform:x64 /optimize+ /debug:pdbonly /utf8output ('/resource:' + (Join-Path $PSScriptRoot 'source/Controls.xaml') + ',DeskCalendar.Controls') ('/resource:' + (Join-Path $PSScriptRoot 'source/google-g.png') + ',DeskCalendar.GoogleIcon') ('/resource:' + $ClientConfig + ',DeskCalendar.OAuthClient') ('/win32icon:' + (Join-Path $PSScriptRoot 'source/app.ico')) ('/win32manifest:' + (Join-Path $PSScriptRoot 'source/app.manifest')) ('/out:' + (Join-Path $PSScriptRoot $OutputName)) $references $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'source/App.config') -Destination ((Join-Path $PSScriptRoot $OutputName)+'.config') -Force
Write-Output (Join-Path $PSScriptRoot $OutputName)
