# Compila e avvia TopoCursor. Premi Ctrl+C per chiuderlo in modo pulito (il cursore torna normale).
$ErrorActionPreference = 'Stop'
dotnet build "$PSScriptRoot\src\Windows\TopoCursor.Windows.csproj" -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $PSScriptRoot 'src\Windows\bin\Release\net8.0-windows\TopoCursor.exe'
$p = Start-Process -FilePath $exe -PassThru
Write-Host "TopoCursor avviato (PID $($p.Id)). Ctrl+C per chiudere." -ForegroundColor Green
try {
    Wait-Process -Id $p.Id
}
finally {
    if (-not $p.HasExited) {
        & $exe --quit
        $p.WaitForExit(3000) | Out-Null
        Write-Host "TopoCursor chiuso." -ForegroundColor Yellow
    }
}
