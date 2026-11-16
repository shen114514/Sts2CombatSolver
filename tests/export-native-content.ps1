param(
    [Parameter(Mandatory=$true)][string]$GameDirectory,
    [Parameter(Mandatory=$true)][string]$DecompilerPath,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
# Read-only game extraction. Output is private development evidence; do not
# redistribute the game's decompiled implementation with this mod.
$assembly=Join-Path $GameDirectory 'data_sts2_windows_x86_64\sts2.dll'
if(!(Test-Path -LiteralPath $assembly -PathType Leaf)){throw 'Game assembly missing'}
if(!(Test-Path -LiteralPath $DecompilerPath -PathType Leaf)){throw 'Existing ilspycmd path required'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a new output directory to preserve prior version evidence'}
New-Item -ItemType Directory -Path $output|Out-Null
$hash=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$version=(& $DecompilerPath --version|Out-String).Trim()
& $DecompilerPath -p -o (Join-Path $output 'native') $assembly *> (Join-Path $output 'decompile.log')
if($LASTEXITCODE){throw "Decompiler failed; preserved log in $output"}
if((Get-FileHash -LiteralPath $assembly).Hash -ne $hash){throw 'Game changed during extraction; do not use this result'}
$index=foreach($category in @('Cards','Relics','Events','Enchantments')){
    $folder=Join-Path $output "native\MegaCrit.Sts2.Core.Models.$category"
    foreach($file in Get-ChildItem -LiteralPath $folder -Filter '*.cs' -File){
        [ordered]@{category=$category;name=$file.BaseName;source=[IO.Path]::GetRelativePath($output,$file.FullName)
            sourceSha256=(Get-FileHash -LiteralPath $file.FullName).Hash}
    }
}
[ordered]@{schema=1;generatedAt=(Get-Date).ToString('o');gameAssemblySha256=$hash;decompiler=$version
    scope='Source-file index in four native namespaces, includes abstract/deprecated classes; concrete runtime inventory is content-audit.json from ENCHANTMENT_CONTRACT.'
    counts=@($index|Group-Object -Property {$_['category']}|ForEach-Object {@{category=$_.Name;files=$_.Count}});models=@($index)
}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $output 'source-index.json') -Encoding utf8
Write-Output "Exported game sources and versioned index to $output"
