$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\bin\Release\Sts2CombatSolver.dll')))
$type=$assembly.GetType('CombatSolver.BattleHpStopPolicy',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
function Net([int]$gross,[int]$healed,[int]$relic){$type.GetMethod('NetHpLoss',$flags).Invoke($null,@($gross,$healed,$relic))}
function Target([bool]$win,[int]$net,[int]$target,[bool]$quick,[int]$hp,[int]$max){$type.GetMethod('MeetsTarget',$flags).Invoke($null,@($win,$net,$target,$quick,$hp,$max))}
$checks=0
function Check([bool]$ok,[string]$name){if(!$ok){throw $name};$script:checks++;Write-Output "PASS $name"}
Check ((Net 1 0 6) -eq 0) 'one damage plus Burning Blood satisfies net zero'
Check ((Net 8 0 6) -eq 2) 'centipede eight gross damage becomes two net damage'
Check ((Net 8 3 6) -eq 0) 'combat and victory healing combine once'
Check ((Net 20 3 6) -eq 11) 'large losses do not become low losses'
Check ((Net 1 0 0) -eq 1) 'no relic means no imaginary healing'
Check (Target $true 0 0 $false 1 80) 'explicit zero target accepts genuine net zero victory'
Check (!(Target $false 0 0 $true 70 80)) 'unfinished frontier never qualifies'
Check (Target $true 8 0 $true 50 80) 'safe low loss auto route accepted'
Check (!(Target $true 9 0 $true 50 80)) 'above eight still searches'
Check (!(Target $true 8 0 $false 50 80)) 'quick mode off honors strict target'
Check (!(Target $true 8 0 $true 11 80)) 'low HP win does not use relaxed threshold'
Check (!(Target $true 8 0 $true 19 100)) 'percentage cushion also required'
Check (Target $true 8 0 $true 20 100) 'exact cushion boundary accepted'
"$checks battle loss stop checks passed"
