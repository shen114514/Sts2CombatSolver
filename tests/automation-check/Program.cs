using CombatSolver;
using SpireStrategist.Core;
int checks=0;
void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
var features=new[]{RoomAutomationFeature.Rewards,RoomAutomationFeature.Shop,RoomAutomationFeature.Events,RoomAutomationFeature.Camp};
for(int mask=0;mask<16;mask++)
{
    var options=new RoomAutomationOptions((mask&1)!=0,(mask&2)!=0,(mask&4)!=0,(mask&8)!=0);
    Check(options.Any==(mask!=0),"enabled group mask "+mask);
    Check(!options.Allows(RoomAutomationFeature.None),"unowned surfaces stay manual "+mask);
    for(int bit=0;bit<4;bit++)Check(options.Allows(features[bit])==((mask&(1<<bit))!=0),"independent feature "+mask+"/"+bit);
}
var gate=new ActionGate();gate.Enable();
Check(!gate.TryClaim("buy",100,true,8000),"initial purchase waits");
Check(!gate.TryClaim("buy",5000,true,8000),"purchase still waiting");
gate.Pause();Check(!gate.TryClaim("buy",12000,true,8000),"disabled gate cannot finish pending purchase");
gate.Enable();Check(!gate.TryClaim("buy",13000,true,8000),"re-enable starts a new delay");
Check(gate.TryClaim("buy",21001,true,8000),"new delay can complete");
Check(!gate.TryClaim("buy",30000,true,8000),"submitted native transaction is not retried");
CardSnapshot Card(string id)=>FoolPolicy.Card(id)??throw new Exception(id);
RunSnapshot state=new(){Character="Fool",PlayerCount=1,Hp=76,MaxHp=77,Gold=175,Act=1,
 Deck=[..Enumerable.Repeat(Card("StrikeFool"),5),..Enumerable.Repeat(Card("DefendFool"),4),Card("FoolishShot"),Card("WahChoKi")]};
Check(CardCatalog.Find(Card("WahChoKi"))!=null,"Fool catalog wiring retained");
Check(CardMechanics.Upgrade(state,Card("WahChoKi"))>0,"contextual camp upgrade is valued");
Check(PriorityShopPolicy.Evaluate(state,[new("bag","BAG_OF_PREPARATION","准备背包",175)],
 [new("filler",Card("ArrowStab"),50)])?.Relic.Key=="bag","strong relic budget survives card competition");
Check(PriorityShopPolicy.Evaluate(state with{Gold=174},[new("bag","BAG_OF_PREPARATION","准备背包",175)],[])==null,"actual affordability retained");
var wuppyVars=new Dictionary<string,double>{{"ImmediateWuppyHp",60},{"CautiousWuppyHp",30},{"HpLoss",state.Hp*.05}};
EventChoice Wuppy(string option)=>EventRules.Describe("WUPPY_IN_TROUBLE","WUPPY_IN_TROUBLE.pages.INITIAL.options."+option,option,state,wuppyVars);
var petOptions=new[]{Wuppy("HELP_IMMEDIATELY"),Wuppy("BE_CAUTIOUS"),Wuppy("IGNORE")};
Check(petOptions.All(c=>c.Supported),"native Wuppy branches no longer force a manual stop");
var petPlan=EventPolicy.Evaluate(state,petOptions);
Check(petPlan.RecommendedKey==petOptions[0].Key||petPlan.RecommendedKey==petOptions[1].Key,
    "healthy deck chooses a pet after comparing curse and life opportunity costs");
Check(!EventRules.Describe("WUPPY_IN_TROUBLE","future-unknown-option","unknown",state,wuppyVars).Supported,"new unknown Wuppy branches stay manual");
Check(EventPolicy.Evaluate(state with{Hp=1},[petOptions[1] with{Lethal=true},petOptions[2]]).RecommendedKey==petOptions[2].Key,
    "native lethal option veto overrides pet value");
Console.WriteLine($"{checks} independent flags, delayed cancellation and decision regression checks passed");
