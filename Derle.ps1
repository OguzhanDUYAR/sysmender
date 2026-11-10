param(
    [string] $Hedef,
    [switch] $Dogrula
)

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:windir 'Microsoft.NET\Framework\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw '.NET Framework C# derleyicisi bulunamadı.'
}

$manifest = Join-Path $PSScriptRoot 'app.manifest'
$script = Join-Path $PSScriptRoot 'Veri\Bakim.ps1'
$remove = Join-Path $PSScriptRoot 'Veri\Kaldirma.ps1'
$translation = Join-Path $PSScriptRoot 'Veri\English.tsv'
$catalog = Join-Path $PSScriptRoot 'Veri\Uygulamalar.tsv'
$license = Join-Path $PSScriptRoot 'WinUtil-MIT.txt'
$icon = Join-Path $PSScriptRoot 'Marka.ico'
$itCatalog = Join-Path $PSScriptRoot 'Veri\BT.tsv'
$itScript = Join-Path $PSScriptRoot 'Veri\BT.ps1'
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | ForEach-Object FullName)
$mode = @('/target:winexe')
if ($Dogrula) {
    $runName = 'Dogrulama-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $runFolder = (New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot $runName)).FullName
    if (!$Hedef) { $Hedef = Join-Path $runFolder 'Dogrula.exe' }
    $mode = @('/target:exe', '/main:BakimMerkezi.Verification')
    $sources += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Testler') -Filter '*.cs' -File | ForEach-Object FullName)
} elseif (!$Hedef) {
    $Hedef = Join-Path $PSScriptRoot 'WindowsBakimMerkezi.exe'
}

$arguments = @(
    '/nologo', '/platform:anycpu', '/langversion:5', '/codepage:65001',
    '/optimize+', '/warn:4', '/warnaserror+', "/out:$Hedef", "/win32manifest:$manifest", "/win32icon:$icon",
    "/resource:$script,Bakim.Bakim.ps1", "/resource:$remove,Bakim.Kaldirma.ps1", "/resource:$catalog,Bakim.Uygulamalar.tsv",
    "/resource:$translation,Bakim.English.tsv",
    "/resource:$itCatalog,Bakim.BT.tsv", "/resource:$itScript,Bakim.BT.ps1",
    "/resource:$license,Bakim.WinUtil-MIT.txt",
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll', '/reference:System.ServiceProcess.dll', '/reference:System.Web.Extensions.dll'
)
& $compiler @mode @arguments @sources
if ($LASTEXITCODE -ne 0) { throw "Derleme hatası (kod $LASTEXITCODE)." }
Write-Host "Oluşturuldu: $Hedef"
if ($Dogrula) {
    & (Get-Item -LiteralPath $Hedef).FullName $runFolder
    if ($LASTEXITCODE -ne 0) { throw "Doğrulama hatası (kod $LASTEXITCODE)." }
}
