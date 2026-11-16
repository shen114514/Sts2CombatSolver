$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\bin\Release\Sts2CombatSolver.dll')))
$type=$assembly.GetType('CombatSolver.CentipedeProgress',$true)
function Profile([int[]]$hp,[int[]]$effective=@()){
 $value=[Activator]::CreateInstance($type)
 for($i=0;$i -lt $hp.Count;$i++){
  $eh=if($effective.Count){$effective[$i]}else{$hp[$i]}
  $value=$type.GetMethod('Add').Invoke($value,@([int]$hp[$i],[int]$eh))
 }
 return $value
}
$checks=0
function Check([bool]$ok,[string]$name){if(!$ok){throw $name};$script:checks++}
$before=Profile @(8,25,25)
$prepared=Profile @(8,16,25)
$tunnel=Profile @(2,25,25)
$knockdown=Profile @(0,25,25) @(25,25,25)
Check $before.CanPrepare 'all-live segment group can stage'
Check ($prepared.PreparationValue -gt $tunnel.PreparationValue) 'ties for largest HP still retain damage against a high-HP segment'
Check ($type.GetMethod('IsPreparation').Invoke($null,@($before,$prepared))) 'nonlethal damage creates a preparation branch'
Check (!$type.GetMethod('IsPreparation').Invoke($null,@($before,$knockdown))) 'a knockdown is not mistaken for preparation'
Check (!$knockdown.CanPrepare -and $knockdown.Reviving -eq 1) 'an open revival window switches away from staging'
$balanced=Profile @(16,16,16)
$skewed=Profile @(1,1,46)
Check ($balanced.Hp -eq $skewed.Hp -and $balanced.BeamAdjustment -gt $skewed.BeamAdjustment) 'equal damage prefers a group closer to a coordinated finish'
Check ($skewed.BeamAdjustment -ge -120000) 'shape guidance is capped at four player HP'
Check ((Profile @(40)).BeamAdjustment -eq 0) 'single-enemy fights receive no adjustment'
Check ((Profile @(0,0,0)).BeamAdjustment -eq 0) 'completed victory receives no staging adjustment'
Check (!(Profile @(0,0,1) @(25,25,1)).CanPrepare) 'last living segment remains a finishing target'
"$checks centipede coordination checks passed"
