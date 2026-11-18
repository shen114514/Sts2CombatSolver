namespace SpireStrategist.Core;

// Exact native hit breakpoints; acquisition and opportunity-cost values are priors.
public static class MyShiniesPolicy
{
    public static bool Owns(RunSnapshot s) => s.Deck.Any(c => c.ClassName == "MyShinies");
    public static int GoldPerHit(CardSnapshot c) => Math.Max(1,
        (int)c.Variables.GetValueOrDefault("Gold", c.UpgradeLevel > 0 ? 10 : 20));
    public static int Hits(CardSnapshot c, int gold) => Math.Max(0, gold) / GoldPerHit(c);
    private static double Strength(RunSnapshot s) => (s.Relics.Any(r => r is "VAJRA" or "RELIC.VAJRA") ? 1 : 0)
        + Math.Max(0, s.GiryaLifts ?? 0) + Math.Min(8, s.Deck.Where(c => c.ClassName != "MyShinies")
            .Select(CardCatalog.Find).OfType<CardProfile>().Sum(p => p.Strength) * 2);
    public static double Damage(RunSnapshot s, CardSnapshot c, int gold)
        => Hits(c, gold) * ((double)c.Variables.GetValueOrDefault("Damage", 1) + Strength(s));
    public static double SpendPenalty(RunSnapshot s, double amount)
    {
        if (amount <= 0 || !Owns(s)) return 0;
        int after = Math.Max(0, s.Gold - (int)Math.Ceiling(amount));
        double loss = s.Deck.Where(c => c.ClassName == "MyShinies")
            .Sum(c => Damage(s, c, s.Gold) - Damage(s, c, after));
        // Keep cash for repeated attacks, but let sufficiently valuable survival purchases win.
        return loss * .65 * Math.Min(1, 20d / Math.Max(1, s.Deck.Length));
    }
    public static double UpgradeValue(RunSnapshot s, CardSnapshot c)
    {
        if (!c.CanUpgrade || c.UpgradeLevel > 0) return 0;
        var upgraded = c with { UpgradeLevel = 1, Variables = new(c.Variables) { ["Gold"] = 10 } };
        return Math.Clamp((Damage(s, upgraded, s.Gold) - Damage(s, c, s.Gold)) * .7, 0, 30);
    }
    public static Evaluation Apply(RunSnapshot s, IReadOnlyList<DecisionOption> options, Evaluation baseline)
    {
        if(s.PlayerCount!=1||s.MaxHp<=0)return baseline;
        var cards = options.Where(o => o.Kind == "card" && o.Card?.ClassName == "MyShinies"
            && !CardCatalog.HasUnmodelledModification(o.Card)).ToDictionary(o => o.Key, o => o.Card!);
        if (cards.Count == 0) return baseline;
        var scores = baseline.Options.Select(o =>
        {
            if (!cards.TryGetValue(o.Key, out var c) || !o.Enabled) return o;
            int hits = Hits(c, s.Gold);
            double value = Math.Round(Math.Clamp(Damage(s, c, s.Gold) / 3.5 - 2
                - s.Deck.Count(d => d.ClassName == "MyShinies") * 2 - Math.Max(0, c.Cost - 1) * 3, -4, 30), 3);
            return o with { Supported = true, Score = value,
                Parts = [new($"我的小钱钱：现款{s.Gold}，每{GoldPerHit(c)}金币一段，共{hits}段；计入多段力量先验", value)],
                Notes = ["保留金币可保留伤害；购买本牌按成交后金币估值。伤害先验不等于完整战斗预测。"] };
        }).ToArray();
        var enabled = scores.Where(o => o.Enabled && !options.Any(x => x.Key == o.Key && MultiplayerAdvice.Ignore(x))).ToArray();
        return baseline with { Options = scores, RecommendedKey = enabled.Length > 0 && enabled.All(o => o.Supported && o.Score.HasValue)
            ? enabled.OrderByDescending(o => o.Score).ThenBy(o => o.Key, StringComparer.Ordinal).First().Key : null };
    }
}
