param(
  [Parameter(Mandatory=$true)][string]$CompilerPath,
  [string]$PayloadDir=(Join-Path $PSScriptRoot '../dist/app'),
  [string]$OutputDir=(Join-Path $PSScriptRoot '../dist')
)
$ErrorActionPreference='Stop'
if(!(Test-Path -LiteralPath $CompilerPath)){throw 'Inno Setup ISCC.exe not found.'}
if(!(Test-Path -LiteralPath (Join-Path $PayloadDir 'DeskCalendar.exe'))){throw 'Prepare the release payload before building an installer.'}
New-Item -ItemType Directory -Path $OutputDir -Force|Out-Null
& $CompilerPath ('/DPayloadDir='+[IO.Path]::GetFullPath($PayloadDir)) ('/DInstallerOutput='+[IO.Path]::GetFullPath($OutputDir)) (Join-Path $PSScriptRoot 'DeskCalendar.iss')
if($LASTEXITCODE-ne0){throw 'Installer compilation failed.'}