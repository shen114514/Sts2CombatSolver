namespace SpireStrategist.Core;

public sealed record ShopRelic(string Key,string Id,string Name,int Price);
public sealed record PriorityPurchase(ShopRelic Relic,ShopCard? Card,string Reason)
{
    public int TotalPrice=>Relic.Price+(Card?.Price??0);
}

// Native-effect models and conditional protected priorities are checked before
// cards/removal. Live affordability and post-purchase re-evaluation remain mandatory.
public static class PriorityShopPolicy
{
    public static double? RelicNetValue(RunSnapshot s,string id,int price,bool finalShop=false)
    {
        if(finalShop&&id.Split('.').Last()=="MEMBERSHIP_CARD")return 0;
        var model=RelicValue.Shop(id,s);
        return model is {Supported:true}?model.Value-new StrategicValue(s).GoldSpend(price,finalShop):null;
    }

    public static PriorityPurchase? Evaluate(RunSnapshot s,ShopRelic[] relics,ShopCard[] cards,int boughtCards=0,int? removalPrice=null,bool finalShop=false)
    {
        if(!DefectStrategy.Supported(s.Character)||s.PlayerCount!=1||s.MaxHp<=0)return null;
        var eligible=relics.Where(r=>r.Price>=0&&r.Price<=s.Gold&&!s.Relics.Contains(r.Id)&&!s.Relics.Contains("RELIC."+r.Id));
        // Buying the discount first changes every remaining price; caller re-reads stock.
        foreach(var id in new[]{"MEMBERSHIP_CARD","GHOST_SEED","MINIATURE_TENT","ICE_CREAM","CHEMICAL_X"})
        {
            if(id=="MINIATURE_TENT"&&s.Act>2)continue;
            foreach(var relic in eligible.Where(r=>r.Id.Replace("RELIC.","")==id).OrderBy(r=>r.Price).ThenBy(r=>r.Key,StringComparer.Ordinal))
            {
                if(MyShiniesPolicy.Owns(s)&&RelicNetValue(s,relic.Id,relic.Price,finalShop) is not >0)continue;
                if(id=="GHOST_SEED")
                {
                    var basics=s.Deck.Count(c=>(c.ClassName is "StrikeIronclad" or "DefendIronclad" or "StrikeDefect" or "DefendDefect" or "StrikeSilent" or "DefendSilent" or "StrikeRegent" or "DefendRegent" or "StrikeNecrobinder" or "DefendNecrobinder" or "StrikeFool" or "DefendFool")&&!c.Keywords.Contains("Ethereal"));
                    if(basics<5||basics/(double)Math.Max(1,s.Deck.Length)<.3)continue;
                    return new(relic,null,$"尚有 {basics} 张未虚无打防，占牌组 {basics/(double)s.Deck.Length:P0}，优先幽灵种子；它只让未打出的打防在回合结束时消耗，不改善首轮抽牌，也不永久删牌。");
                }
                if(id=="MEMBERSHIP_CARD"&&s.Act>2)
                {
                    var usefulCards=ShopPolicy.Evaluate(s,cards,null,s.Gold,boughtCards,0).Choices
                        .Where(c=>c.Kind=="card"&&c.Score>0).Sum(c=>c.Price);
                    var usefulRelics=relics.Where(r=>r.Key!=relic.Key&&RelicValue.Shop(r.Id,s) is {Value:>0}).Sum(r=>r.Price);
                    double recoverable=Math.Min(s.Gold-relic.Price,(usefulCards+usefulRelics)/2d);
                    if(recoverable<relic.Price)continue;
                    return new(relic,null,$"当前店内已建模目标的折扣预算可回收会员卡价格（预计省 {recoverable:F0}，卡价 {relic.Price}）；先买卡再重读折后库存。");
                }
                if(id!="CHEMICAL_X")return new(relic,null,id switch
                {
                    "MEMBERSHIP_CARD"=>"前两幕优先打折卡，购买后按实际折后价格重算商店。",
                    "MINIATURE_TENT"=>"前两幕优先帐篷，提高后续火堆多项行动收益。",
                    _=>"优先冰淇淋，保留跨回合能量。"
                });
                if(s.Deck.Any(c=>c.CostsX))return new(relic,null,"牌组已有 X 牌，优先化学物 X。");
                // A card must be worthwhile on its own, before granting Chemical X synergy.
                var cardPlan=ShopPolicy.Evaluate(s,cards.Where(c=>c.Card.CostsX).ToArray(),null,s.Gold,boughtCards,0);
                var choice=cardPlan.Choices.Where(c=>c.Kind=="card"&&c.Score>0&&c.Price+relic.Price<=s.Gold)
                    .OrderByDescending(c=>c.Score).FirstOrDefault();
                if(choice!=null)return new(relic,cards.Single(c=>c.Key==choice.Key),
                    "先确认 X 牌本身值得购买，且现款足够同时购买该牌与化学物 X；先买牌再买遗物。");
            }
        }
        // Compare the live relic budget BEFORE committing money to cards/removal.
        // A merchant has at most three ordinary relics. Enumerating their subsets
        // prevents one filler purchase from breaking an affordable stronger bundle.
        var modeled=eligible.Select(r=>(Relic:r,Net:RelicNetValue(s,r.Id,r.Price,finalShop)))
            .Where(x=>x.Net>0).OrderByDescending(x=>x.Net).ThenBy(x=>x.Relic.Price).Take(10).ToArray();
        var baseline=ShopPolicy.Evaluate(s,cards,removalPrice,s.Gold,boughtCards,finalShop?0:null);
        var alternative=baseline.Recommendation;
        var alternativeCard=cards.FirstOrDefault(c=>c.Key==alternative?.Key)?.Card;
        var profile=alternativeCard==null?null:CardCatalog.Find(alternativeCard);
        bool engineCard=alternativeCard!=null&&(alternativeCard.Type=="Power"
            || profile is {Scaling:>0} || profile is {Energy:>=.8} || profile is {Draw:>=1}
            || alternativeCard.ClassName=="Quiver"&&s.Deck.Count(c=>c.ClassName.EndsWith("Shot"))>=3);
        bool urgentRemoval=alternative is {Kind:"remove",DeckIndex:>=0}
            && alternative.DeckIndex<s.Deck.Length&&s.Deck[alternative.DeckIndex].Type=="Curse";
        var protectedRelic=modeled.FirstOrDefault(x=>HasProtectedPriority(x.Relic.Id,s));
        if(protectedRelic.Relic!=null&&!engineCard&&!urgentRemoval)
            return new(protectedRelic.Relic,null,$"高优先级遗物「{protectedRelic.Relic.Name}」的当前条件已满足，净收益 {protectedRelic.Net:F1}；先锁定遗物预算，普通输出/防御填充牌不抢占。成交后重新评估。");
        double bestTotal=Math.Max(0,baseline.Recommendation?.Score??0);
        int bestMask=0;
        for(int mask=1;mask<(1<<modeled.Length);mask++)
        {
            int cost=0;double value=0;
            for(int i=0;i<modeled.Length;i++)if((mask&(1<<i))!=0){cost+=modeled[i].Relic.Price;value+=modeled[i].Net!.Value;}
            if(cost>s.Gold)continue;
            var remaining=s with{Gold=s.Gold-cost};
            var next=ShopPolicy.Evaluate(remaining,cards,removalPrice,s.Gold,boughtCards,finalShop?0:null);
            value+=Math.Max(0,next.Recommendation?.Score??0);
            if(value>bestTotal+.001){bestTotal=value;bestMask=mask;}
        }
        if(bestMask!=0)
        {
            var first=modeled.Where((_,i)=>(bestMask&(1<<i))!=0).First();
            return new(first.Relic,null,$"先保住当前遗物购买组合，净收益 {bestTotal:F1} 高于先买牌/删牌 {Math.Max(0,baseline.Recommendation?.Score??0):F1}；本件净收益 {first.Net:F1}。"+
                (finalShop?"后方已无商店，不再为以后购物扣留金币；仍核对现款。":"正收益遗物可使用预留金币。")+"成交后重读实际价格和库存。");
        }
        return null;
    }

    private static bool HasProtectedPriority(string id,RunSnapshot s)
        => id.Split('.').Last() is "BAG_OF_PREPARATION" or "BEATING_REMNANT" or "TUNGSTEN_ROD"
            or "POCKETWATCH" or "GAMBLING_CHIP" or "KUNAI" or "SHURIKEN"
            or "MUMMIFIED_HAND" or "CONDUCTORS_BATON" or "FERTILIZER" or "TWINE" or "WHITTLING_KNIFE"
            && RelicValue.Shop(id,s) is {Value:>=12};
}
