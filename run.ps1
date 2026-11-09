# Builds and starts TopoCursor. Press Ctrl+C to close it cleanly (the normal pointer comes back).
$ErrorActionPreference = 'Stop'
dotnet build "$PSScriptRoot\src\Windows\TopoCursor.Windows.csproj" -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $PSScriptRoot 'src\Windows\bin\Release\net8.0-windows\TopoCursor.exe'
$p = Start-Process -FilePath $exe -PassThru
Write-Host "TopoCursor started (PID $($p.Id)). Ctrl+C to close." -ForegroundColor Green
try {
    Wait-Process -Id $p.Id
}
finally {
    if (-not $p.HasExited) {
        & $exe --quit
        $p.WaitForExit(3000) | Out-Null
        Write-Host "TopoCursor closed." -ForegroundColor Yellow
    }
}
