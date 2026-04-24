<#
.SYNOPSIS
  Installerer OpenObserve for development mode
  Nb. Use generated token feature described in issue 7022
#>

$env:ZO_ROOT_USER_EMAIL = Read-Host "Enter your email"
$env:ZO_ROOT_USER_PASSWORD = Read-Host "password"
$env:ZO_ROOT_USER_TOKEN = Read-Host "token"
$env:ZO_DATA_DIR = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\otel_storage'))

# Use email:password (not email:token). The token form authenticates through
# the service-account code path and fails OpenFGA checks on query endpoints
# such as POST /_search. The password form triggers the root-user OpenFGA
# bypass, which this dev instance needs for full read access.
# Dev-only: the password is the hardcoded "password" above.
$AuthNotEncoded = "$($env:ZO_ROOT_USER_EMAIL):$($env:ZO_ROOT_USER_PASSWORD)"
$AuthEncoded = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($AuthNotEncoded))

$TestAppProject = "$PSScriptRoot\..\testapp\testapp.csproj"

# Ensure the project has a UserSecretsId so `set` has somewhere to write.
# `init` is a no-op if one already exists.
dotnet user-secrets init --project $TestAppProject | Out-Null

echo 'Setting dotnet secret in web project with following authentication token:'
echo $AuthEncoded
dotnet user-secrets set "Telemetry:Headers" "Authorization=Basic $($AuthEncoded), stream-name=default, organization=default" --project $TestAppProject

echo 'Initial start of openobserve telemetry server at http://localhost:5080/'
echo 'Nb. For subsequent runs just execute openobserve.exe'


c:\tools\openobserve\openobserve.exe