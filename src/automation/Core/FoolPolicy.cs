using System.Text.Json;

namespace SpireStrategist.Core;

// 愚者（Fool，0.113 新增的第 6 个角色）的选牌/商店/营火先验。
//
// 机制底盘（0.113 实测）：
//   * 箭矢：PlayerCombatState.Arrows（ArrowSlot，状态 Retrieved/Away/Destroyed），
//           CharacterModel.BaseArrowSlotCount 决定槽数，ArrowCmd.ShootArrowAtTarget 射出，
//           射中后敌人挂 ImpaledPower（扎穿），可以被「松脱 / 摧毁」。
//   * 瞄准(Aim) / 拉伸(Stretch)：PowerModel，走通用能力通道。
//   * 悲伤(SADNESS，状态牌) / 喜悦(JOY，token) / 旋律(哇·啾·叽) / 塔罗牌：卡牌生成与联动。
//
// 数据来源：data/fool-cards-v0.113.0.json（游戏内 /cards?format=definitions 导出）
//           data/fool-draft-v0.1.json（**规则生成**的基础先验，不是胜率拟合，见 status 字段）。
//
// 设计原则：宁可标 Unsupported，也不要拿别的角色的权重硬套 —— 未知效果要明确写出来，
// 这样玩家能看出「这张牌还没建模」，而不是被一个假分数误导。
public static class FoolPolicy
{
    public const string Version = "fool-draft-0.4-trial";
    public const string Scope = "愚者接管：选牌、营火、路线、商店与原生事件；战斗交给 CombatSolver。"
        + "箭矢体系尚未在求解器里完整建模，未建模效果会明确标注而不是假装有分。";

    private sealed record Profile(double Base, double[] Phase, double Duplicate, double UpgradeDelta, string[] Tags);
    private sealed record Coefficients(double CardTax, double DamageNeed, double BlockNeed, double DuplicateQuadratic, double SynergyCap);
    private sealed record Prior(Dictionary<string, Profile> Cards, Coefficients Coefficients);
    private sealed record Definition(string MultiplayerConstraint, CardSnapshot Basic, CardSnapshot Upgraded);

    private static T Read<T>(string resource)
    {
        using var stream = typeof(FoolPolicy).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException(resource);
        return JsonSerializer.Deserialize<T>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static readonly Prior prior = Read<Prior>("SpireStrategist.FoolPrior.json");
    private static readonly Dictionary<string, Definition> definitions =
        Read<Definition[]>("SpireStrategist.FoolCards.json").ToDictionary(d => d.Basic.ClassName);

    public static CardSnapshot? Card(string name, bool upgraded = false)
        => definitions.TryGetValue(name, out var d) ? (upgraded ? d.Upgraded : d.Basic) : null;

    private static bool Has(CardSnapshot c, string tag)
        => !MultiplayerAdvice.Ignore(c) && prior.Cards.TryGetValue(c.ClassName, out var p) && p.Tags.Contains(tag);

    private static bool Any(CardSnapshot c, params string[] tags) => tags.Any(t => Has(c, t));

    private static double Var(CardSnapshot c, string name, double fallback = 0)
        => c.Variables.TryGetValue(name, out var v) ? (double)v : fallback;

    // Exact shooting family from current game CardTag.Shoot, not the broad prior "arrow" tag.
    private static readonly HashSet<string> ShootingCards = new(StringComparer.Ordinal)
    {
        "DivineShot", "FoolishShot", "HeavyShot", "QuickShot", "RainShot", "MelodicShot",
        "RepeatShot", "SkyShot", "BlossomShot", "HobblingShot", "RapidShot", "TrickShot",
    };

    private static int Count(IEnumerable<CardSnapshot> cards, string tag) => cards.Count(c => Has(c, tag));

    public static bool Tag(CardSnapshot c, string tag) => Has(c, tag);

    public static bool MultiplayerCard(string name)
        => definitions.TryGetValue(name, out var d) && d.MultiplayerConstraint == "MultiplayerOnly";

    public static double? UpgradeDelta(CardSnapshot c)
        => prior.Cards.TryGetValue(c.ClassName, out var p) ? p.UpgradeDelta : null;

    /// <summary>给 CardCatalog 的通用链用：把愚者的先验映射成通用 CardProfile。</summary>
    public static CardProfile? ProfileFor(CardSnapshot c)
    {
        if (MultiplayerAdvice.Ignore(c)) return null;
        if (!prior.Cards.TryGetValue(c.ClassName, out var p)) return null;
        return new(p.Base,
            Attack: Var(c, "Damage") / 10,
            Block: Var(c, "Block") / 10,
            Draw: Has(c, "draw") ? Math.Max(.3, (Var(c, "Cards", 1) - 1) * .6) : 0,
            Energy: Has(c, "energy") ? Var(c, "Energy", 1) * .6 : 0,
            Scaling: Has(c, "scaling") ? .7 : 0,
            ExhaustSource: Has(c, "exhaust") ? 1 : 0,
            Upgrade: 2 + p.UpgradeDelta * 3);
    }

    public static Evaluation Evaluate(RunSnapshot state, IReadOnlyList<DecisionOption> options)
    {
        var warnings = new List<string>
        {
            Scope,
            "愚者先验是规则生成的 v0.1（按伤害/格挡/抽牌与机制关键词换算），不是胜率、也没有实战校准。",
            "箭矢在战斗模拟里尚未完整建模：选牌建议只看卡面机制，不代表求解器会把它打出满价值。",
        };

        var unknown = state.Deck
            .Where(c => !prior.Cards.ContainsKey(c.ClassName) && c.Type is not ("Curse" or "Status"))
            .Select(c => c.Name).Distinct().ToArray();
        if (unknown.Length > 0) warnings.Add("牌组有未适配卡牌，能力评估不完整：" + string.Join("、", unknown));

        var valid = state.Character == "Fool" && state.PlayerCount >= 1 && state.MaxHp > 0;
        if (state.IsMultiplayer) { warnings.Remove(Scope); warnings.Add(MultiplayerAdvice.Notice); }

        var ignored = options.Where(MultiplayerAdvice.Ignore).Select(o => o.Key).ToHashSet();
        OptionScore Unsupported(DecisionOption o, string note) => new(o.Key, o.Name, false, o.Enabled, null, [], [note]);

        var scores = options.Select(o =>
            !valid ? Unsupported(o, "仅支持单人愚者有效局内状态。") :
            !o.Enabled ? new OptionScore(o.Key, o.Name, true, false, null, [], ["当前不可选。"]) :
            ignored.Contains(o.Key) ? Unsupported(o, MultiplayerAdvice.Ignored) :
            o.Kind == "skip" ? new OptionScore(o.Key, o.Name, true, true, 0, [], []) :
            o.Kind == "reroll" ? new OptionScore(o.Key, o.Name, true, true, 0, [], ["原生免费重掷仅在没有已建模正收益选项时使用；不预知重掷结果。"]) :
            o.Kind == "card" && o.Card != null ? Score(state, o, o.Card) :
            DefectStrategy.NonCard(state, o) ?? Unsupported(o, "此类选择尚未适配愚者，请手动处理。")
        ).ToArray();

        var active = scores.Where(s => s.Enabled && !ignored.Contains(s.Key)).ToArray();
        var skips = options.Where(o => o.Kind == "skip").Select(o => o.Key).ToHashSet();
        var rerolls = options.Where(o => o.Kind == "reroll").Select(o => o.Key).ToHashSet();
        string? best = active.Length > 0 && active.All(s => s.Supported && s.Score.HasValue) ?
            active.OrderByDescending(s => s.Score).ThenByDescending(s => rerolls.Contains(s.Key))
                  .ThenByDescending(s => skips.Contains(s.Key)).ThenByDescending(s => s.Key, StringComparer.Ordinal)
                  .First().Key : null;
        if (best == null) warnings.Add("存在未适配选项，暂不作整体推荐；已适配选项的分数仅供参考。");
        return new(Version, best, scores, warnings.ToArray());
    }

    /// <summary>自动接管拿不到整体推荐时的兜底：只在「全是卡牌/跳过/重掷」时挑一个已建模的正收益项。</summary>
    public static DecisionOption? KnownFreeReward(IReadOnlyList<DecisionOption> options, Evaluation evaluation)
    {
        var active = options.Where(o => o.Enabled).ToArray();
        if (!active.Any(o => o.Kind == "skip") || active.Any(o => o.Kind is not ("card" or "skip" or "reroll")))
            return null;
        return active.Select(o => (Option: o, Score: evaluation.Options.FirstOrDefault(e => e.Key == o.Key)))
            .Where(x => x.Score is { Supported: true, Enabled: true, Score: not null } && x.Score.Score >= 0)
            .OrderByDescending(x => x.Score!.Score).ThenByDescending(x => x.Option.Kind == "reroll")
            .ThenByDescending(x => x.Option.Kind == "skip").ThenBy(x => x.Option.Key, StringComparer.Ordinal)
            .Select(x => x.Option).FirstOrDefault();
    }

    private static OptionScore Score(RunSnapshot s, DecisionOption o, CardSnapshot c)
    {
        var name = c.ClassName;
        OptionScore Unsupported(string reason) => new(o.Key, o.Name, false, o.Enabled, null, [], [reason]);

        if (!prior.Cards.TryGetValue(name, out var p))
            return Unsupported("尚未建立愚者语境模型（这张牌不在 fool-draft 先验里）。");
        if (definitions.TryGetValue(name, out var def) && def.MultiplayerConstraint == "MultiplayerOnly")
            return Unsupported("多人专用牌，不参与单人建议。");
        if (CardCatalog.HasUnmodelledModification(c)
            || (!string.IsNullOrEmpty(c.Enchantment) && c.Enchantment != "ENCHANTMENT.SWIFT"))
            return Unsupported("附魔／异变未建模，不能据此推荐。");

        var deck = s.Deck;
        var parts = new List<ScorePart>();
        void Add(string reason, double value)
        {
            if (value != 0) parts.Add(new(reason, Math.Round(value, 3)));
        }

        var act = Math.Clamp(s.Map?.ContentAct is > 0 ? s.Map.ContentAct : s.Act, 1, 3);
        var k = prior.Coefficients;

        Add("基础先验（规则生成，未拟合）", p.Base);
        Add("前／中／后期需求", p.Phase[Math.Clamp(act - 1, 0, 2)]);
        Add("占用一次抽牌的基准成本", -k.CardTax);

        bool T(string tag) => p.Tags.Contains(tag);

        if (!T("basic"))
        {
            var nonbasic = deck.Where(d => d.Rarity != "Basic").ToArray();
            // Printed damage alone made an expensive attack look like a complete damage engine.
            // Count repeatable damage per energy, and discount shots competing for one arrow.
            double AttackEfficiency(CardSnapshot d)
            {
                double damage = Var(d, "Damage");
                if (damage <= 0 || d.CostsX) return 0;
                double repeat = Math.Max(1, Var(d, "Repeat", Var(d, "HitCount", 1)));
                double efficiency = damage * repeat / Math.Max(1, d.Cost);
                if (ShootingCards.Contains(d.ClassName)
                    && deck.Count(x => ShootingCards.Contains(x.ClassName)) > 2
                    && !deck.Any(x => x.ClassName == "Quiver")) efficiency *= .5;
                return efficiency;
            }
            var offense = nonbasic.Where(d => Has(d, "attack"))
                .Sum(d => Math.Min(1.5, AttackEfficiency(d) / 8));
            var defense = nonbasic.Sum(d => Math.Min(1.5, Var(d, "Block") / 8));
            var arrowCards = Count(deck, "arrow");
            var slotCards = Count(deck, "slot");
            var aimSources = Count(deck, "aim_source");
            int shots = deck.Count(d => ShootingCards.Contains(d.ClassName));
            int quivers = deck.Count(d => d.ClassName == "Quiver");
            if (ShootingCards.Contains(name))
            {
                // Without extra arrow capacity, more shots compete for the same unavailable arrow.
                int surplus = Math.Max(0, shots - (2 + quivers * 2));
                Add("射击密度超过箭槽与回收支撑（试验规则）", -Math.Min(6, surplus * 2.0));
            }
            if (T("slot") && shots >= 3)
                Add("多张射击需要补箭槽而非继续堆射击", Math.Min(3, shots - 2));
            if (name == "Jest")
            {
                bool sadnessPayoff = deck.Any(d => d.ClassName is "AllIsWell" or "Anguish" or "Growth");
                Add("每次打出生成保留悲伤，长战堵手代价", sadnessPayoff ? -1.0 : -4.0);
            }
            var melodies = Count(deck, "melody");
            var sorrow = Count(deck, "sorrow");

            if (T("attack"))
            {
                double efficiency = AttackEfficiency(c);
                Add("每费输出缺口", k.DamageNeed * Math.Max(0, 1 - offense / 4) * Math.Min(1.5, efficiency / 8));
                if (act == 1 && efficiency >= 8 && c.Cost <= 1)
                    Add("第一幕高效即时输出", Math.Min(2, (efficiency - 6) * .35));
            }
            if (T("block") || T("stretch"))
                Add("防御缺口", k.BlockNeed * Math.Max(0, 1 - defense / 5) * 0.5);
            if (T("aoe") && Count(deck, "aoe") == 0) Add("牌组还没有群体输出", 1.0);
            if (T("draw")) Add("手牌周转", 0.6 * Math.Min(2, Math.Max(0, Var(c, "Cards", 1) - 1)));

            // 箭矢体系：没有箭槽/射击牌时，射击类卡片的实际价值大打折扣。
            if (T("arrow") && arrowCards == 0 && slotCards == 0)
                Add("箭矢体系缺件：牌组里还没有箭矢来源", -1.5);
            if (T("arrow") && aimSources == 0)
                Add("没有瞄准来源，射击吃不到增伤", -0.8);
            if (T("impale") && arrowCards == 0)
                Add("扎穿必须先有射击牌", -0.8);
            if (T("aim_source") && arrowCards == 0)
                Add("没有射击牌时瞄准无处可用", -1.0);
            if (T("melody") && melodies <= 1)
                Add("旋律种类还少", -0.5);
            if ((T("sorrow") || T("joy")) && sorrow == 0 && Count(deck, "joy") == 0)
                Add("没有配套的悲伤/喜悦联动", -0.4);
        }

        var same = deck.Count(d => d.ClassName == name);
        if (same > 0) Add("重复份数的边际递减", -(p.Duplicate * same * 0.35 * (1 + k.DuplicateQuadratic)));

        if (!T("basic") && deck.Length > 16)
        {
            // New utility must pay for delaying existing good cards; reliable cycling is discounted less.
            double dilution = (deck.Length - 16) * .35;
            if (T("draw") && Var(c, "Cards") >= 2 && c.Cost <= 1) dilution *= .35;
            Add("抽牌稀释：已有牌组需要更高加牌门槛", -Math.Min(4, dilution));
        }
        if (c.UpgradeLevel == 0 && c.CanUpgrade) Add("可升级", 0.2);
        if (c.Cost >= 3 && !c.CostsX) Add("高费卡的手牌压力", -0.4);

        double score = parts.Sum(x => x.Value);
        string[] notes = p.Tags.Length > 0 ? [$"机制标签：{string.Join("/", p.Tags)}"] : [];
        return new OptionScore(o.Key, o.Name, true, o.Enabled, Math.Round(score, 2), parts.ToArray(), notes);
    }
}
