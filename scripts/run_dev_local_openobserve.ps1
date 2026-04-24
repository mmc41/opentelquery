$env:ZO_DATA_DIR = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\otel_storage'))
c:\tools\openobserve.exe