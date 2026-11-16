namespace SpireStrategist.Core;

// 0.113.1 Glam: Times=1, first play in each combat only. Amount does not
// multiply Times. Keep the character's own card model and unknown-card veto.
internal static class GlamRewardPolicy
{
    internal static Evaluation Evaluate(RunSnapshot state, IReadOnlyList<DecisionOption> options,
        Func<RunSnapshot, IReadOnlyList<DecisionOption>, Evaluation> evaluateBase)
    {
        var glam = options.Where(o => o.Kind == "card" && o.Card is { Enchantment: "ENCHANTMENT.GLAM", Affliction: null })
            .ToDictionary(o => o.Key, o => o.Card!);
        var plain = options.Select(o => glam.TryGetValue(o.Key, out var card)
            ? o with { Card = card with { Enchantment = null, EnchantmentAmount = 0, Modified = false } } : o).ToArray();
        var baseline = evaluateBase(state, plain);
        var scores = baseline.Options.Select(score =>
        {
            if (!glam.TryGetValue(score.Key, out var card) || !score.Supported || !score.Score.HasValue)
                return score;
            // No safe marginal estimate for variable resources or unplayable cards.
            if (card.CostsX || card.CostsStarX || card.Type is "Curse" or "Status")
                return score with { Supported = false, Score = null,
                    Notes = ["华彩的可变费用／不可打出牌额外结算尚未估值。"] };
            double bonus = Math.Round(Math.Clamp((score.Score.Value + 2.5 + Math.Max(0, card.Cost) * 1.2) * .65, -5, 10), 3);
            return score with { Score = Math.Round(score.Score.Value + bonus, 3),
                Parts = [.. score.Parts, new("华彩每场首次打出追加一次结算（有限先验，不按无限重放）", bonus)],
                Notes = [.. score.Notes, "华彩按当前版本每场首次重放一次估值，未做完整战斗模拟。"] };
        }).ToArray();
        var ignored = options.Where(MultiplayerAdvice.Ignore).Select(o => o.Key).ToHashSet();
        var enabled = scores.Where(s => s.Enabled && !ignored.Contains(s.Key)).ToArray();
        string? best = enabled.Length > 0 && enabled.All(s => s.Supported && s.Score.HasValue)
            ? enabled.OrderByDescending(s => s.Score).ThenByDescending(s => options.Single(o => o.Key == s.Key).Kind == "skip")
                .ThenBy(s => s.Key, StringComparer.Ordinal).First().Key : null;
        return baseline with { RecommendedKey = best, Options = scores,
            Warnings = [.. baseline.Warnings, "华彩收益只计每场首次结算；未知卡及其他未适配异变继续明确标记。"] };
    }
}
