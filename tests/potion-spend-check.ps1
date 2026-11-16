# Run with PowerShell 7 (.NET 9 or newer), after building the mod.
$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\bin\Release\Sts2CombatSolver.dll')))
$policy=$assembly.GetType('CombatSolver.PotionSpendPolicy',$true)
$relief=$assembly.GetType('CombatSolver.BossHpRelief',$true)
$cases=@(@('None',0,$false,$false,'Normal'),@('ActClearHeal',0,$false,$false,'Normal'),
 @('RunEnding',0,$false,$false,'SpendAll'),@('RunEnding',9,$false,$false,'SpendAll'),
 @('None',10,$true,$false,'Normal'),@('RunEnding',10,$true,$false,'Normal'),@('RunEnding',10,$true,$true,'SpendAll'))
foreach($case in $cases){
 $actual=$policy.GetMethod('Resolve').Invoke($null,@([Enum]::Parse($relief,$case[0]),[int]$case[1],[bool]$case[2],[bool]$case[3]))
 if($actual.ToString() -ne $case[4]){throw ('Potion spend mode mismatch: '+($case -join ','))}
}
'7 potion spend context checks passed'
