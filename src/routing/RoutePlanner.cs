namespace CombatSolver.Routing;

public sealed record RoutePoint(string Id,string Kind,string[] Children);
public sealed record RouteContext(int Hp,int MaxHp,int Gold,int Act,double Readiness);
public static class RoutePlanner
{
    // Small bounded map projection: no hidden encounters, shop rolls or RNG reads.
    // HP and opportunities are recomputed at every real map selection.
    public static string? Choose(RouteContext context,RoutePoint[] points,IReadOnlyCollection<string> legal)
    {
        if(context.MaxHp<=0||context.Hp<=0)return null;
        var graph=points.ToDictionary(p=>p.Id);
        double Visit(string id,double hp,int gold,int depth,HashSet<string> seen)
        {
            if(depth>=10||!graph.TryGetValue(id,out var point)||!seen.Add(id))return 0;
            double damage=0,score=0;
            double readiness=Math.Clamp(context.Readiness,0,1);
            switch(point.Kind)
            {
                case "Monster":damage=(5+context.Act*3)*(1.25-.55*readiness);score=24+8*readiness;gold+=15;break;
                case "Elite":damage=(14+context.Act*6)*(1.4-.7*readiness);score=64+15*readiness-40*Math.Max(0,.6-readiness);gold+=30;break;
                case "RestSite":var heal=Math.Min(context.MaxHp-hp,context.MaxHp*.3);hp+=heal;score=14+heal*2.2;break;
                case "Shop":score=gold>=150?Math.Min(75,20+gold*.12):6;gold=Math.Max(0,gold-150);break;
                case "Treasure":score=65;break;
                case "Unknown":damage=3+context.Act;score=28;break;
                case "Ancient":score=50;break;
                case "Boss":return hp<=Math.Max(8,context.MaxHp*.2)?-150:0;
                default:score=10;break;
            }
            hp-=damage;
            score-=damage*1.25;
            if(hp<=0)score-=1000;
            else if(hp<context.MaxHp*.25)score-=(context.MaxHp*.25-hp)*4;
            if(depth==0&&point.Kind=="Elite"&&hp<context.MaxHp*.25)score-=180;
            var future=point.Children.Select(child=>Visit(child,hp,gold,depth+1,new(seen))).DefaultIfEmpty(0).Max();
            return score+.82*future;
        }
        return legal.Where(graph.ContainsKey).Order(StringComparer.Ordinal)
            .Select(id=>(Id:id,Score:Visit(id,context.Hp,context.Gold,0,[])))
            .OrderByDescending(x=>x.Score).Select(x=>x.Id).FirstOrDefault();
    }
}
