param(
    [Parameter(Mandatory=$true)][string]$GameDirectory,
    [Parameter(Mandatory=$true)][string]$ProcDumpPath,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [ValidateSet('ENCHANTMENT_CONTRACT','GLAM_SAVED','AUTO_NEXT','CATASTROPHE','CATASTROPHE_SAVED')][string]$Mode='ENCHANTMENT_CONTRACT',
    [string]$ReplaySave
)
$ErrorActionPreference='Stop'
if(@(Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue).Count){throw 'Close the game normally first; preserve its current run'}
$dir=[IO.Path]::GetFullPath($EvidenceDirectory)
if(Test-Path -LiteralPath $dir){throw 'New evidence directory required'}
if($Mode -in @('GLAM_SAVED','CATASTROPHE_SAVED') -and (!$ReplaySave -or !(Test-Path -LiteralPath $ReplaySave -PathType Leaf))){throw 'An existing saved-run COPY is required'}
if(!(Test-Path -LiteralPath $ProcDumpPath -PathType Leaf)){throw 'Existing ProcDump executable required'}
New-Item -ItemType Directory -Path (Join-Path $dir 'dumps') -Force|Out-Null
$vars=@{
    COMBATSOLVER_SELF_TEST_DIR=$dir
    COMBATSOLVER_SELF_TEST_CHARACTER='DEFECT'
    COMBATSOLVER_SELF_TEST_MODE=$Mode
    COMBATSOLVER_SELF_TEST_SOURCE_ROOT=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src'))
    COMBATSOLVER_REPLAY_SAVE=$ReplaySave
}
$old=@{}
try{
    foreach($key in $vars.Keys){$old[$key]=[Environment]::GetEnvironmentVariable($key,'Process');[Environment]::SetEnvironmentVariable($key,$vars[$key],'Process')}
    $exe=Join-Path $GameDirectory 'SlayTheSpire2.exe'
    $p=Start-Process -FilePath $ProcDumpPath -WorkingDirectory $GameDirectory -ArgumentList @('-accepteula','-mm','-e','-g','-x',"`"$dir\dumps`"","`"$exe`"") -WindowStyle Hidden -PassThru -RedirectStandardOutput "$dir\stdout.log" -RedirectStandardError "$dir\stderr.log"
    @{mode=$Mode;started=(Get-Date).ToString('o');dumperPid=$p.Id
        dllHash=(Get-FileHash -LiteralPath (Join-Path $GameDirectory 'mods\Sts2CombatSolver\Sts2CombatSolver.dll')).Hash
        replaySaveHash=$(if($ReplaySave){(Get-FileHash -LiteralPath $ReplaySave).Hash}else{$null})
    }|ConvertTo-Json|Set-Content -LiteralPath "$dir\launch.json" -Encoding utf8
    Write-Output "Started non-saving $Mode; evidence: $dir"
}finally{foreach($key in $old.Keys){[Environment]::SetEnvironmentVariable($key,$old[$key],'Process')}}
# Asynchronous. A result alone is insufficient: also require shouldSave=false,
# native process exit 0 in stdout.log, and zero crash dumps before declaring pass.
