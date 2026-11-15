using CombatSolver.Routing;
int checks=0;
void Check(bool passed,string label){if(!passed)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
var context=new RouteContext(80,100,0,1,.8);
RoutePoint[] options=[new("elite","Elite",[]),new("camp","RestSite",[]),new("shop","Shop",[]),new("fight","Monster",[]),new("chest","Treasure",[])];
Check(RoutePlanner.Choose(context with{Hp=10},options,["elite","camp"])=="camp","low HP selects recovery before dangerous elite");
Check(RoutePlanner.Choose(context,options,["fight","shop"])=="fight","empty wallet does not overvalue a shop");
Check(RoutePlanner.Choose(context with{Gold=400},options,["fight","shop"])=="shop","cash changes useful shop priority");
Check(RoutePlanner.Choose(context with{Hp=100},options,["elite","chest"])=="chest","safe treasure beats avoidable damage");
Check(RoutePlanner.Choose(context,options,["elite"])=="elite","only actual legal roots can be selected");
Check(RoutePlanner.Choose(context,options,[])==null,"no legal root yields no input");
Check(RoutePlanner.Choose(context with{Hp=0},options,["camp"])==null,"ended run cannot select a route");
Check(RoutePlanner.Choose(context,[new("a","Unknown",["a"])],["a"])=="a","cyclic malformed map is bounded");
Check(RoutePlanner.Choose(context,options,["missing"])==null,"unknown map point is not clicked");
Console.WriteLine($"{checks} route checks passed");
