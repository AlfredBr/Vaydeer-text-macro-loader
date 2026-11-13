$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
dotnet build "$PSScriptRoot\src\Vaydeer.Cli\Vaydeer.Cli.csproj" -c Release -o "$PSScriptRoot\bin" --nologo
if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }
