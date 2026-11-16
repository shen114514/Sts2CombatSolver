using System.Text.Json;
using SpireStrategist.Core;
int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
CardSnapshot C(string name,bool up=false)=>DefectPolicy.Card(name,up)??throw new Exception(name);
RunSnapshot starter=new(){Character="Defect",PlayerCount=1,Hp=65,MaxHp=75,Energy=3,Act=1,
 Relics=["RELIC.CRACKED_CORE"],Deck=[..Enumerable.Repeat(C("StrikeDefect"),4),..Enumerable.Repeat(C("DefendDefect"),4),C("Zap"),C("Dualcast")]};
double Score(RunSnapshot state,string name,bool up=false)=>DefectPolicy.Evaluate(state,[new("c",name,"card",Card:C(name,up))]).Options[0].Score??throw new Exception(name);
Check(Score(starter,"Coolheaded")>0,"first unupgraded Coolheaded is a useful channel/cantrip");
Check(Score(starter,"LightningRod")>Score(starter,"DoubleEnergy"),"delayed channel beats unsupported energy in starter deck");
var frost=starter with{Act=2,Deck=[..starter.Deck,C("Glacier"),C("Coolheaded",true),C("ColdSnap"),C("Hologram",true)]};
Check(Score(frost,"Defragment")>Score(starter with{Deck=[C("StrikeDefect"),C("DefendDefect")]},"Defragment"),"Focus gains value with existing orb production");
Check(Score(frost,"Loop")>0,"repeatable Frost passive supports the first Loop without two Focus cards");
var physical=starter with{Act=2,Deck=[..starter.Deck,C("Claw"),C("Claw"),C("Claw"),C("Hologram",true),C("AllForOne")]};
Check(Score(physical,"Claw")>Score(physical,"Defragment"),"established physical engine remains viable");
Check(Score(frost,"Coolheaded")>Score(frost with{Deck=[..frost.Deck,C("Coolheaded"),C("Coolheaded")]},"Coolheaded"),"orb draw still has diminishing duplicate value");
Check(Score(starter with{Deck=[..starter.Deck,C("Sunder"),C("Skim"),C("MeteorStrike")]},"DoubleEnergy")>Score(starter,"DoubleEnergy"),"energy has real spending outlets");
if(args.Length>0)
{
 using var doc=JsonDocument.Parse(File.ReadAllText(args[0]));
 var options=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
 var replay=new List<object>();
 foreach(var row in doc.RootElement.GetProperty("decisions").EnumerateArray())
 {
  var d=row.GetProperty("data");if(!d.TryGetProperty("kind",out var kind)||kind.GetString()!="cards")continue;
  var state=d.GetProperty("state").Deserialize<RunSnapshot>(options)!;
  var choices=d.GetProperty("options").Deserialize<DecisionOption[]>(options)!;
  var evaluation=DefectPolicy.Evaluate(state,choices);
  var ranked=evaluation.Options.Where(x=>x.Score.HasValue).OrderByDescending(x=>x.Score).ToArray();
  replay.Add(new{floor=state.Floor,options=choices.Select(c=>new{card=c.Card?.ClassName??c.Kind,score=evaluation.Options.Single(x=>x.Key==c.Key).Score,chosen=c.Key==evaluation.RecommendedKey})});
  Check(ranked.All(x=>double.IsFinite(x.Score!.Value)),"finite real reward scores");
 }
 File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!,"draft-after.json"),JsonSerializer.Serialize(replay,new JsonSerializerOptions{WriteIndented=true}));
}
Console.WriteLine($"{checks} Defect orb, physical compatibility and observed reward checks passed");
