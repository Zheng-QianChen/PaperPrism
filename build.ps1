param(
    [switch]$SelfTest,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

$NoRestore = if (Test-Path "$RepoRoot\src\EInkSmartInvert\obj\project.assets.json") { "--no-restore" } else { "" }

Write-Host "==> Building PaperPrism (Release)..." -ForegroundColor Cyan
dotnet build "$RepoRoot\PaperPrism.sln" -c Release --nologo $NoRestore

if ($SelfTest) {
    $TestOut = Join-Path $RepoRoot "test-output"
    Write-Host "==> Running GPU Smart Invert Self-Test..." -ForegroundColor Cyan
    dotnet "$RepoRoot\src\EInkSmartInvert\bin\Release\net9.0-windows\PaperPrism.dll" --test-render "$TestOut"
}

if ($Publish) {
    $PubDir = Join-Path $RepoRoot "publish"
    if (Test-Path $PubDir) {
        Get-ChildItem $PubDir -Filter "*.old" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
        foreach ($lockedName in @("PaperPrism.exe", "PaperPrism.dll", "PaperPrism.pdb", "EInkSmartInvert.exe", "EInkSmartInvert.dll", "EInkSmartInvert.pdb")) {
            $full = Join-Path $PubDir $lockedName
            if (Test-Path $full) {
                try {
                    $fs = [IO.File]::Open($full, 'Open', 'ReadWrite', 'None')
                    $fs.Close()
                } catch {
                    Move-Item $full "$full.$([DateTime]::Now.Ticks).old" -Force -ErrorAction SilentlyContinue
                }
            }
        }
    }
    Write-Host "==> Publishing standalone Release folder to $PubDir ..." -ForegroundColor Cyan
    dotnet publish "$RepoRoot\src\EInkSmartInvert\EInkSmartInvert.csproj" `
        -c Release `
        -o "$PubDir" `
        --nologo $NoRestore
    Write-Host "==> Done! Launch: $PubDir\PaperPrism.exe" -ForegroundColor Green
}
