namespace SpireStrategist.Core;

// Marginal orb support uses present channels, draw and energy outlets. It does
// not require an orb payoff before accepting its enabling card, or vice versa.
internal static class DefectOrbPlan
{
    internal static IEnumerable<ScorePart> Adjustments(RunSnapshot s, CardSnapshot c)
    {
        int N(params string[] names) => s.Deck.Count(d => names.Contains(d.ClassName));
        bool Tag(CardSnapshot d, params string[] tags) => tags.Any(t => DefectPolicy.Tag(d,t));
        var frost = s.Deck.Count(d => Tag(d,"frost","frost2","frost3"));
        var channels = s.Deck.Count(d => Tag(d,"frost","frost2","frost3","lightning","dark","glass"));
        var focus = s.Deck.Count(d => DefectPolicy.Tag(d,"focus") && !DefectPolicy.Tag(d,"temp_focus"));
        var copies = N(c.ClassName);
        bool physical = DefectDamagePlan.PhysicalCommitted(s) && DefectComposition.Physical(s)>DefectComposition.Orbs(s);
        if(c.ClassName=="Coolheaded" && copies<2)
            yield return new("产冰同时自替换可持续运转，按现有集中与冰球配合",(physical?.4:.8)+Math.Min(1.2,focus*.6+frost*.15));
        if(DefectPolicy.Tag(c,"focus") && !DefectPolicy.Tag(c,"temp_focus") && channels>0 && !physical)
            yield return new("永久集中跨回合同时放大产球与持续防守",Math.Min(3.5,channels*.5+frost*.35)/(1+focus*.7));
        if(c.ClassName=="LightningRod" && !physical)
            yield return new("避雷针提供后续两回合产电，格挡之外另计延迟供球",(1.4+Math.Min(1,focus*.5))/(1+copies*.8));
        if(c.ClassName=="DoubleEnergy")
        {
            var outlets=s.Deck.Count(d=>d.Cost>=2||d.CostsX||DefectPolicy.Tag(d,"draw")&&d.Variables.GetValueOrDefault("Cards")>=2);
            if(outlets<2)yield return new("缺少高费或净过牌出口，额外能量不能代替产球与输出",-(2-outlets)*1.25);
        }
        // 永久集中只留一到两张：连胜攻略把"唯一长线集中"列为顶级，但同一份牌组里堆第三、
        // 第四张时收益已经饱和，后面只是占抽牌位。2.5×已有张数让第二张 −2.5、第三张 −5。
        if(DefectPolicy.Tag(c,"focus") && !DefectPolicy.Tag(c,"temp_focus") && focus>=1)
            yield return new("永久集中已够用，第二张起按饱和降权",-2.5*focus);
        // 过渡牌上限：攻略明确"过渡牌再优质也不能抓很多，会影响后期牌组质量"。
        // 一层 4 张、二层 6 张封顶，超出后继续加过渡牌额外降权。
        var act=s.Map?.ContentAct is >0? s.Map.ContentAct : s.Act;
        var transition=s.Deck.Count(d=>DefectPolicy.Tag(d,"transition"));
        var transitionCap=act<=1?4:act==2?6:8;
        if(DefectPolicy.Tag(c,"transition") && transition>=transitionCap)
            yield return new($"过渡牌已达 {transitionCap} 张上限，继续增加会拖低后期牌组质量",
                -2.5-Math.Min(3,transition-transitionCap));
    }
}
