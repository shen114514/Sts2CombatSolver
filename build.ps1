param([Parameter(Mandatory=$true)][string]$GameDirectory,[switch]$Package,[switch]$PackageOnly)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$game=[IO.Path]::GetFullPath($GameDirectory)
$version=(Get-Content -LiteralPath "$root\Sts2CombatSolver.json" -Raw -Encoding UTF8|ConvertFrom-Json).version
if(!(Test-Path -LiteralPath "$game\data_sts2_windows_x86_64\sts2.dll")){throw 'Select a Windows Slay the Spire 2 installation'}
if(!$PackageOnly){
    dotnet build "$root\Sts2CombatSolver.csproj" -c Release --nologo -v minimal "-p:Sts2Dir=$game"
    if($LASTEXITCODE -ne 0){throw 'Mod build failed'}
}
if(!$Package){return}
$release=Join-Path $root 'dist'
New-Item -ItemType Directory -Path $release -Force|Out-Null
$stage=Join-Path $release 'payload'
New-Item -ItemType Directory -Path $stage -Force|Out-Null
foreach($file in @('Sts2CombatSolver.dll','Sts2CombatSolver.json','LICENSE')){
    $from=if($file -like '*.dll'){Join-Path $root "bin\Release\$file"}else{Join-Path $root $file}
    Copy-Item -LiteralPath $from -Destination (Join-Path $stage $file) -Force
}
$zip=Join-Path $release "Sts2CombatSolver-$version-win-x64.zip"
if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip}
Compress-Archive -LiteralPath "$stage\Sts2CombatSolver.dll","$stage\Sts2CombatSolver.json","$stage\LICENSE" -DestinationPath $zip
$payloadHash=(Get-FileHash -LiteralPath $zip).Hash
$installerSource=(Get-Content -LiteralPath "$root\installer\Installer.cs" -Raw -Encoding UTF8).Replace('__PAYLOAD_HASH__',$payloadHash).Replace('__VERSION__',$version)
$generated=Join-Path $release 'Installer.generated.cs'
[IO.File]::WriteAllText($generated,$installerSource,[Text.UTF8Encoding]::new($true))
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(!(Test-Path -LiteralPath $csc)){throw '.NET Framework 4.x compiler is required to build the Windows installer'}
$exe=Join-Path $release "Sts2CombatSolver-Setup-$version.exe"
& $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$exe" /r:System.Windows.Forms.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/resource:$zip,payload.zip" $generated
if($LASTEXITCODE -ne 0){throw 'Installer build failed'}
@($zip,$exe)|ForEach-Object { $hash=Get-FileHash -LiteralPath $_; "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))" }|Set-Content -LiteralPath "$release\SHA256SUMS.txt" -Encoding ascii
Get-Content -LiteralPath "$release\SHA256SUMS.txt"
