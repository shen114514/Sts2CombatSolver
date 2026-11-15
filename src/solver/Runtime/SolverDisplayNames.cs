using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using CombatSolver.Engine.InCombat.Simulation;
using Godot;
using System.Reflection;

namespace CombatSolver;

internal sealed class SolverDisplayNames
{
    /// <summary>
    /// 0.113.0 的 <c>CardModel.Title</c> 每次访问都会走 <c>LocManager.SmartFormat</c>；构建全卡表
    /// （每张牌 × 每个升级等级）要几千次，实测把主线程钉死 60 秒以上、堆涨到数 GB。
    /// 默认改用 <c>Id.Entry</c> 当显示名（自动打牌不受影响，只是界面里不显示中文译名）。
    /// 想要本地化名字就设环境变量 <c>STS2_LOCALIZED_NAMES=1</c>，但要接受首次搜索的卡顿。
    /// </summary>
    private static readonly bool UseLocalizedTitles =
        string.Equals(System.Environment.GetEnvironmentVariable("STS2_LOCALIZED_NAMES"), "1", StringComparison.Ordinal);

    private static string? _canonicalCardLanguage;
    private static Dictionary<(string Id, int Upgrade), string>? _canonicalCardNames;

    private readonly Dictionary<(string Id, int Upgrade), string> _cards;
    private readonly Dictionary<string, string> _potions;
    private readonly Dictionary<string, string> _relics;
    private readonly Dictionary<string, string> _powers;
    private readonly Dictionary<string, string> _orbs;
    private readonly bool _english;
    private readonly Dictionary<string, string> _monsters;
    private readonly Dictionary<uint, string> _creatures;
    private readonly Dictionary<string, int> _slotOrder;
    private readonly bool _positioned;
    private static readonly PropertyInfo EncounterSlotsProperty = typeof(NCombatRoom).GetProperty(
        "EncounterSlots", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    private SolverDisplayNames(
        Dictionary<(string Id, int Upgrade), string> cards,
        Dictionary<string, string> potions,
        Dictionary<string, string> relics,
        Dictionary<string, string> powers,
        Dictionary<string, string> orbs,
        Dictionary<string, string> monsters,
        Dictionary<uint, string> creatures,
        Dictionary<string, int> slotOrder,
        bool positioned,
        bool english)
    {
        _cards = cards;
        _potions = potions;
        _relics = relics;
        _powers = powers;
        _orbs = orbs;
        _english = english;
        _monsters = monsters;
        _creatures = creatures;
        _slotOrder = slotOrder;
        _positioned = positioned;
    }

    public static SolverDisplayNames Capture(CombatState state)
    {
        Player player = LocalContext.GetMe(state)
            ?? throw new InvalidOperationException("找不到本地玩家。");
        PlayerCombatState combat = player.PlayerCombatState
            ?? throw new InvalidOperationException("找不到玩家战斗状态。");
        IEnumerable<CardModel> cards = combat.Hand.Cards
            .Concat(combat.DrawPile.Cards)
            .Concat(combat.DiscardPile.Cards)
            .Concat(combat.ExhaustPile.Cards)
            .Concat(combat.PlayPile.Cards);
        Dictionary<(string Id, int Upgrade), string> cardNames = CanonicalCardNames();
        foreach (CardModel card in cards)
            CaptureCardTitles(cardNames, card, overwrite: true);

        Dictionary<string, string> monsterNames = new(StringComparer.Ordinal);
        foreach (MonsterModel monster in ModelDb.Monsters)
            monsterNames.TryAdd(monster.Id.Entry, SafeTitle(monster.Title, monster.Id.Entry));
        Dictionary<uint, string> creatureNames = [];
        NCombatRoom? room = NCombatRoom.Instance;
        Control? slots = room == null ? null : (Control?)EncounterSlotsProperty.GetValue(room);
        Dictionary<string, int> slotOrder = [];
        IReadOnlyList<string> encounterSlots = state.Encounter?.Slots ?? [];
        // 只声明在原生场景里的槽位（如 GremlinMercNormal 的 merc/sneaky/fat）不在模型
        // Encounter.Slots 中；没有模型槽位时按场景 Marker 的横坐标冻结顺序。
        IEnumerable<string> orderedSlots = slots == null ? encounterSlots
            : encounterSlots.Count > 0
                ? encounterSlots.OrderBy(slot => slots.GetNode<Marker2D>(slot).GlobalPosition.X)
                : slots.FindChildren("*", nameof(Marker2D), recursive: true, owned: false)
                    .OfType<Marker2D>()
                    .OrderBy(marker => marker.GlobalPosition.X)
                    .Select(marker => marker.Name.ToString());
        foreach (string slot in orderedSlots) slotOrder.TryAdd(slot, slotOrder.Count);
        Dictionary<string, int> enemyTypeCounts = state.Enemies
            .GroupBy(creature => CaptureCreatureBaseName(creature, monsterNames), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Dictionary<string, int> enemyTypeNumbers = new(StringComparer.Ordinal);
        foreach (Creature creature in state.Creatures.OrderBy(creature => NCombatRoom.Instance?.GetCreatureNode(creature)?.GlobalPosition.X ?? 0))
        {
            if (creature.CombatId is not uint combatId)
                continue;
            string baseName = CaptureCreatureBaseName(creature, monsterNames);
            if (creature.Side == CombatSide.Enemy
                && enemyTypeCounts.GetValueOrDefault(baseName) > 1)
            {
                string typeKey = baseName;
                int number = enemyTypeNumbers.GetValueOrDefault(typeKey) + 1;
                enemyTypeNumbers[typeKey] = number;
                bool positioned = NCombatRoom.Instance?.GetCreatureNode(creature) != null;
                string position = positioned
                    ? LocManager.Instance.Language is "zhs" or "zht" ? $"左起{number}" : $"#{number} from left"
                    : $"#{number}";
                creatureNames[combatId] = $"{baseName}（{position}）";
            }
            else
            {
                creatureNames[combatId] = baseName;
            }
        }
        // A player can legally hold more than one potion of the same model. Display names are
        // type-scoped, while planning and deployment continue to distinguish the actual slots.
        Dictionary<string, string> potionNames = new(StringComparer.Ordinal);
        foreach (PotionModel potion in player.Potions)
            potionNames.TryAdd(potion.Id.Entry, potion.Title.GetFormattedText());
        Dictionary<string, string> relicNames = new(StringComparer.Ordinal);
        foreach (RelicModel relic in player.Relics)
            relicNames.TryAdd(relic.Id.Entry, relic.Title.GetFormattedText());
        Dictionary<string, string> powerNames = new(StringComparer.Ordinal);
        foreach (PowerModel power in ModelDb.AllPowers)
        {
            string title = SafeTitle(power.Title, power.Id.Entry);
            powerNames.TryAdd(power.Id.Entry, title);
            powerNames.TryAdd(power.GetType().Name, title);
        }
        Dictionary<string, string> orbNames = new(StringComparer.Ordinal);
        foreach (OrbModel orb in ModelDb.Orbs)
        {
            string title = SafeTitle(orb.Title, orb.Id.Entry);
            orbNames.TryAdd(orb.Id.Entry, title);
            orbNames.TryAdd(orb.GetType().Name, title);
        }
        return new SolverDisplayNames(cardNames, potionNames, relicNames, powerNames, orbNames, monsterNames, creatureNames,
            slotOrder, room != null,
            LocManager.Instance.Language is not ("zhs" or "zht"));
    }

    public string Card(CardModel card)
        => _cards.GetValueOrDefault((card.Id.Entry, card.CurrentUpgradeLevel), card.Id.Entry);

    private static string SafeTitle(LocString title, string fallback)
    {
        try { return title.GetRawText(); }
        catch (LocException) { return fallback; }
    }

    public string Card(string cardId, int upgradeLevel = 0)
        => _cards.GetValueOrDefault((cardId, upgradeLevel), cardId);

    private static Dictionary<(string Id, int Upgrade), string> CanonicalCardNames()
    {
        string language = LocManager.Instance.Language;
        if (_canonicalCardNames == null || !string.Equals(_canonicalCardLanguage, language, StringComparison.Ordinal))
        {
            // 0.113.0：原做法是对每个 canonical 卡 clone + UpgradeInternal 反复升级来收集
            // 「(id, 升级等级) → 名字」，会真的跑游戏侧升级逻辑（还带 Harmony 补丁），
            // 实测把主线程钉死 60 秒以上、堆涨到数 GB。这里不再碰卡牌对象，直接按 id 建表。
            Dictionary<(string Id, int Upgrade), string> names = [];
            foreach (CardModel canonical in ModelDb.AllCards)
            {
                string id = canonical.Id.Entry;
                string baseName;
                if (UseLocalizedTitles)
                {
                    try { baseName = canonical.Title; }
                    catch { baseName = id; }
                }
                else
                {
                    baseName = id;
                }

                names[(id, 0)] = baseName;
                for (var level = 1; level <= 10; level++)
                    names[(id, level)] = baseName + new string('+', level);
            }
            _canonicalCardNames = names;
            _canonicalCardLanguage = language;
        }

        return new Dictionary<(string Id, int Upgrade), string>(_canonicalCardNames);
    }

    private static void CaptureCardTitles(
        IDictionary<(string Id, int Upgrade), string> names,
        CardModel source,
        bool overwrite)
    {
        // 0.113.0：这里原来 clone + UpgradeInternal 逐级收集名字。0.113 的升级会真的跑一遍
        // 游戏侧升级逻辑（带 Harmony 补丁），每张牌都这样走一遍会把主线程钉死（实测 60s+、堆数 GB）。
        // 不显示本地化名时，直接用 id + 升级后缀，完全不碰卡牌对象。
        if (!UseLocalizedTitles)
        {
            int level = source.CurrentUpgradeLevel;
            (string Id, int Upgrade) plainKey = (source.Id.Entry, level);
            if (overwrite || !names.ContainsKey(plainKey))
                names[plainKey] = level <= 0
                    ? source.Id.Entry
                    : source.Id.Entry + new string('+', level);
            return;
        }

        CardModel card = source.IsMutable
            ? (CardModel)source.ClonePreservingMutability()
            : source.ToMutable();
        while (true)
        {
            (string Id, int Upgrade) key = (card.Id.Entry, card.CurrentUpgradeLevel);
            if (overwrite || !names.ContainsKey(key))
                names[key] = UseLocalizedTitles ? card.Title : card.Id.Entry;
            if (!card.IsUpgradable)
                return;
            card.UpgradeInternal();
            card.FinalizeUpgradeInternal();
        }
    }

    public string Potion(PotionModel potion)
        => _potions.GetValueOrDefault(potion.Id.Entry, potion.Id.Entry);

    public string Potion(string potionId)
        => _potions.GetValueOrDefault(potionId, potionId);

    public string Relic(string relicId)
        => _relics.GetValueOrDefault(relicId, relicId);

    public string Monster(string monsterId)
        => _monsters.GetValueOrDefault(monsterId, monsterId);

    public string DamageSource(CombatDamageSource source)
        => source.Kind switch
        {
            CombatDamageSourceKind.Card => Card(source.Id ?? (_english ? "Card" : "卡牌")),
            CombatDamageSourceKind.Potion => Potion(source.Id ?? (_english ? "Potion" : "药水")),
            CombatDamageSourceKind.Relic => Relic(source.Id ?? (_english ? "Relic" : "遗物")),
            CombatDamageSourceKind.Power => _powers.GetValueOrDefault(source.Id ?? string.Empty, source.Id ?? (_english ? "Power" : "能力")),
            CombatDamageSourceKind.Poison => _powers[nameof(PoisonPower)],
            CombatDamageSourceKind.Thorns => _powers[nameof(ThornsPower)],
            CombatDamageSourceKind.Orb => _orbs.GetValueOrDefault(source.Id ?? string.Empty, source.Id ?? (_english ? "Orb" : "球")),
            CombatDamageSourceKind.MonsterMove => _english ? "Enemy move" : "敌方行动",
            _ => _english ? "Unknown effect" : "未知效果",
        };

    public string Creature(Creature? creature) => Creature(creature, null);

    public string Creature(Creature? creature, IReadOnlyList<Creature>? knownEnemies)
    {
        if (creature is null)
            return string.Empty;
        string fallback = creature.Monster?.Id.Entry is { } monsterId
            ? Monster(monsterId)
            : creature.Player?.Character?.Id.Entry ?? "玩家";
        if (creature.CombatId is uint id && _creatures.TryGetValue(id, out string? captured))
            return captured;
        if (knownEnemies == null || creature.Side != CombatSide.Enemy)
            return fallback;
        // Slot ranks are frozen from the native scene. Unslotted encounters use
        // insertion order, as native PositionEnemies does. KnownEnemies retains
        // dead participants so the labels stay stable through the route. A predicted
        // summon may carry a scene-only slot beyond the frozen ranks; it keeps the
        // roster insertion order after known slots instead of failing the name read.
        var peers = knownEnemies.Where(enemy => CaptureCreatureBaseName(enemy, _monsters) == fallback)
            .OrderBy(enemy => enemy.SlotName is { } slot
                ? _slotOrder.GetValueOrDefault(slot, int.MaxValue)
                : 0).ToArray();
        int index = Array.FindIndex(peers, enemy => enemy.CombatId == creature.CombatId);
        if (index < 0) throw new InvalidOperationException("生成敌人缺少所属预测阵容，无法确定显示位置。");
        string position = _positioned
            ? _english ? $"#{index + 1} from left" : $"左起{index + 1}"
            : $"#{index + 1}";
        return $"{fallback}（{position}）";
    }

    private static string CreatureTypeKey(Creature creature)
        => creature.Monster?.Id.Entry ?? creature.Player?.Character?.Id.Entry ?? "PLAYER";

    private static string CaptureCreatureBaseName(
        Creature creature,
        IReadOnlyDictionary<string, string> monsterNames)
        => creature.Monster?.Id.Entry is { } monsterId
            ? monsterNames.GetValueOrDefault(monsterId, monsterId)
            : creature.Name;
}
