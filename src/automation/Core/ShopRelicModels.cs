namespace SpireStrategist.Core;

// Audited against the native v0.113.1 catalog. These are bounded mechanism priors,
// not win probabilities. Acquisition-only and future rewards lose value near the end.
public static class ShopRelicModels
{
    public static RelicAssessment? Evaluate(string id, RunSnapshot s)
    {
        id = id.Split('.').Last();
        var v = new StrategicValue(s);
        int Count(Func<CardSnapshot,bool> predicate) => s.Deck.Count(predicate);
        int attacks = Count(c => c.Type == "Attack"), skills = Count(c => c.Type == "Skill");
        int powers = Count(c => c.Type == "Power"), cheapAttacks = Count(c => c.Type == "Attack" && c.Cost <= 1);
        int shots = Count(c => c.ClassName.EndsWith("Shot") || c.ClassName == "TheShot");
        int zero = Count(c => c.Cost == 0 && !c.CostsX), expensive = Count(c => c.Cost >= 2 && !c.CostsX);
        int junk = Count(c => c.Type is "Curse" or "Status" || c.Rarity == "Basic");
        int sadness = Count(c => c.ClassName is "Jest" or "SorrowfulStrike");
        double density(int n) => Math.Min(3, n * 5d / Math.Max(5, s.Deck.Length));
        double future = Math.Clamp((v.Remaining - 2) / 12, 0, 2);
        double fights = Math.Clamp(v.Remaining * .45, .5, 10);
        double Upgrade(string type)
        {
            var pool = s.Deck.Where(c => c.Type == type && c.CanUpgrade).ToArray();
            return pool.Length == 0 ? 0 : pool.Average(v.Upgrade) * Math.Min(2, pool.Length);
        }
        bool Has(string name) => s.Deck.Any(c => c.ClassName == name);
        int melodies = (s.Deck.Any(c => c.ClassName.StartsWith("Wah")) ? 1 : 0)
            + (s.Deck.Any(c => c.ClassName.StartsWith("Cho") || c.ClassName is "WahCho" or "WahChoKi") ? 1 : 0)
            + (s.Deck.Any(c => c.ClassName.StartsWith("Ki") || c.ClassName is "ChoKi" or "WahChoKi") ? 1 : 0);
        double? value = id switch
        {
            "BAG_OF_PREPARATION" => 12 + Math.Min(3, Math.Max(0, s.Deck.Length - 15) / 5d),
            "BAG_OF_MARBLES" => 5 + 3 * density(attacks),
            "ANCHOR" => 8 * Math.Min(1.5, fights),
            "LANTERN" => 9 + Math.Min(5, expensive),
            "HAPPY_FLOWER" => 10 + Math.Min(4, expensive),
            "CANDELABRA" => 10 + Math.Min(4, expensive),
            "CHANDELIER" => 10 + Math.Min(5, expensive),
            "VENERABLE_TEA_SET" => 3 + 6 * Math.Min(1.5, future) + (v.Has("ICE_CREAM") ? 4 : 0),
            "BREAD" => 5 + Math.Min(7, expensive) - (s.Hp < s.MaxHp * .4 ? 5 : 0),
            "ICE_CREAM" => 12 + Math.Min(8, expensive + v.Profiles.Sum(p => p.Energy)),
            "NUNCHAKU" => 5 + 4 * density(attacks),
            "PENDULUM" => 8 + Math.Min(4, s.Deck.Length / 10d),
            "CENTENNIAL_PUZZLE" => 12,
            "POCKETWATCH" => Math.Clamp(19 - zero * 1.2 - Math.Max(0, s.Energy - 3) * 3, 2, 19),
            "GAMBLING_CHIP" => 12 + Math.Min(6, junk * .6),
            "UNCEASING_TOP" => Math.Clamp(3 + zero * 1.7 + Math.Max(0, s.Energy - 3) * 3
                - s.Deck.Count(c => c.Type is "Curse" or "Status") * 3 - sadness * 2
                - (v.Has("RUNIC_PYRAMID") ? 12 : 0), 0, 20),
            "LETTER_OPENER" => 4 + 5 * density(skills),
            "TUNING_FORK" => 5 + 4 * density(skills),
            "KUNAI" or "SHURIKEN" => 3 + 6 * density(cheapAttacks),
            "ORNAMENTAL_FAN" => 3 + 4 * density(cheapAttacks),
            "KUSARIGAMA" => 3 + 4 * density(cheapAttacks),
            "PEN_NIB" => 4 + 3 * density(attacks) + Math.Min(4, expensive),
            "VAJRA" => 4 + 3 * density(attacks),
            "ODDLY_SMOOTH_STONE" => 4 + 3 * density(Count(c => c.Variables.GetValueOrDefault("Block") > 0)),
            "STRIKE_DUMMY" => 2 * Count(c => c.ClassName.Contains("Strike")),
            "MINIATURE_CANNON" => 2 + 2 * Count(c => c.Type == "Attack" && c.UpgradeLevel > 0),
            "MYSTIC_LIGHTER" => 4 * Count(c => c.Type == "Attack" && !string.IsNullOrEmpty(c.Enchantment)),
            "AKABEKO" => 8 + Math.Min(4, v.Multi * 2),
            "FESTIVE_POPPER" => 10,
            "MERCURY_HOURGLASS" => 12,
            "STONE_CALENDAR" => 8 + (s.Act >= 2 ? 5 : 0),
            "BEATING_REMNANT" => 30,
            "TUNGSTEN_ROD" => 22 + Math.Min(8, v.Profiles.Sum(p => p.SelfDamage) * 3),
            "STURDY_CLAMP" => 5 + Math.Min(13, v.Block * 2),
            "GORGET" => 14,
            "HORN_CLEAT" => 12,
            "CAPTAINS_WHEEL" => 12,
            "RED_MASK" => 11,
            "BRONZE_SCALES" => 8,
            "ORICHALCUM" => v.Block < 3 ? 12 : 5,
            "VAMBRACE" => 4 + Math.Min(10, s.Deck.Select(c => (double)c.Variables.GetValueOrDefault("Block")).DefaultIfEmpty().Max() * .6),
            "PARRYING_SHIELD" => v.Block >= 5 ? 12 : 3,
            "INTIMIDATING_HELMET" => 3 + Math.Min(14, expensive * 2.5),
            "RIPPLE_BASIN" or "ART_OF_WAR" => Math.Clamp(12 - density(attacks) * 4, 1, 10),
            "CLOAK_CLASP" => 4 + Math.Min(8, v.Draw + Count(c => c.Keywords.Contains("Retain"))) - sadness,
            "RAINBOW_RING" => powers > 0 && attacks > 0 && skills > 0 ? 16 : 1,
            "SPARKLING_ROUGE" => 11,
            "MUMMIFIED_HAND" => powers == 0 ? 0 : 5 + Math.Min(20, powers * 3 + expensive),
            "GAME_PIECE" => powers == 0 ? 0 : 4 + Math.Min(16, powers * 3),
            "PERMAFROST" => powers == 0 ? 0 : 7 + Math.Min(4, powers),
            "BELLOWS" => 6 + Math.Min(12, s.Deck.Count(c => c.CanUpgrade) * .8),
            "RAZOR_TOOTH" => 8 + Math.Min(12, s.Deck.Count(c => c.CanUpgrade)),
            "STONE_CRACKER" => (Upgrade("Attack") + Upgrade("Skill")) / 2,
            "WAR_PAINT" => Upgrade("Skill"),
            "WHETSTONE" => Upgrade("Attack"),
            "FROZEN_EGG" => (5 + Math.Min(7, powers * 2)) * future,
            "TOXIC_EGG" => (7 + Math.Min(5, density(skills))) * future,
            "MOLTEN_EGG" => (6 + Math.Min(5, density(attacks))) * future,
            "LASTING_CANDY" => (5 + Math.Min(6, powers)) * future,
            "LAVA_LAMP" => 7 * future,
            "PRAYER_WHEEL" => 12 * future,
            "WHITE_STAR" => 9 * future,
            "WING_CHARM" => 14 * future,
            "BLOOD_VIAL" => Math.Min(18, 2 * fights),
            "MEAT_ON_THE_BONE" => (s.Hp <= s.MaxHp * .65 ? 10 : 5) * Math.Min(2, fights),
            "LEES_WAFFLE" => v.Heal(s.MaxHp) + v.MaxHp(7),
            "MANGO" => v.MaxHp(14), "PEAR" => v.MaxHp(10), "STRAWBERRY" => v.MaxHp(7),
            "ETERNAL_FEATHER" => Math.Min(20, s.Deck.Length / 5d * 3 * future),
            "REGAL_PILLOW" => (s.Hp < s.MaxHp * .65 ? 9 : 3) * future,
            "PANTOGRAPH" => Math.Min(Math.Max(0, s.MaxHp - s.Hp), 25) * .65 + 5 * future,
            "MEAL_TICKET" => 7 * future,
            "PLANISPHERE" => 6 * future,
            "DRAGON_FRUIT" => 10 * future,
            "BOOK_OF_FIVE_RINGS" => 6 * future,
            "WHITE_BEAST_STATUE" => 12 * future,
            "POTION_BELT" => 5 + Math.Min(10, s.PotionSlots.Count(p => p != null) * 2 + (v.Has("PETRIFIED_TOAD") ? 5 : 0)),
            "PETRIFIED_TOAD" => 10 + 3 * future,
            "REPTILE_TRINKET" => s.PotionSlots.Any(p => p != null) ? 8 : 2,
            "BELT_BUCKLE" => s.PotionSlots.All(p => p == null) ? 12 : 2,
            "CAULDRON" => 4 * s.PotionSlots.Count(p => p == null),
            "TINY_MAILBOX" => 6 * future,
            "LIZARD_TAIL" => 22,
            "GREMLIN_HORN" => 10 + 2 * future,
            "SLING_OF_COURAGE" => 8 * future,
            "EXERCISE_MAT" => s.Character == "Fool" ? 9 + (Has("ChoKi") || Has("ChoFirm") ? 4 : 0) : 0,
            "TWINE" => shots > 0 ? 10 + Math.Min(8, density(attacks) * 2) : 0,
            "WHITTLING_KNIFE" => shots > 0 ? 10 + Math.Min(10, shots + v.Draw) : 0,
            "CONDUCTORS_BATON" => melodies == 3 ? 22 : 0,
            "FERTILIZER" => sadness > 0 || v.Has("BIZARRE_LAMP") ? 12 + Math.Min(10, sadness * 3) : 0,
            "BIZARRE_LAMP" => s.Character == "Fool" ? 5 + (Has("Growth") || Has("AllIsWell") ? 7 : 0) : 0,
            "OCARINA" => s.Character == "Fool" ? 8 : 3,
            "THE_ABACUS" => 5 + Math.Min(10, v.Draw * 2) + (s.Deck.Length <= 15 ? 4 : 0),
            "BURNING_STICKS" => 3 + Math.Min(12, v.Exhaust * 2 + (v.Has("TOASTY_MITTENS") ? 5 : 0)),
            "JOSS_PAPER" => 2 + Math.Min(14, v.Exhaust * 2),
            "SHOVEL" => 9 * future,
            "GIRYA" => (s.GiryaLifts ?? 0) >= 3 ? 0 : 9 * future,
            "RINGING_TRIANGLE" => 5 + Math.Min(5, expensive),
            "SCREAMING_FLAGON" => Math.Clamp(4 + zero * 2 - v.Draw - sadness * 3, 0, 15),
            "UNSETTLING_LAMP" => Count(c => c.Variables.Keys.Any(k => k is "VulnerablePower" or "WeakPower" or "PoisonPower")) > 0 ? 10 : 0,
            "GNARLED_HAMMER" => v.BestEnchant("SHARP", 3, 3),
            "KIFUDA" => v.BestEnchant("SWIFT", 1, 3),
            "DOLLYS_MIRROR" => Math.Max(0, s.Deck.Select(v.Add).DefaultIfEmpty().Max()) * 2,
            "ORRERY" => 8 * Math.Min(1.5, future),
            "TOOLBOX" or "VEXING_PUZZLEBOX" => 7,
            "DINGY_RUG" => 3 * future,
            // Discount, tent, X-card bundles stay in the existing priority rules.
            "MEMBERSHIP_CARD" => 18 * future,
            "MINIATURE_TENT" => 22 * future,
            "CHEMICAL_X" => s.Deck.Any(c => c.CostsX) ? 18 : 0,
            "GHOST_SEED" => 2 * v.Basics,
            // Native random-enchantment selection needs its actual rolled enchantment.
            "ROYAL_STAMP" or "PUNCH_DAGGER" => null,
            _ => null,
        };
        return value.HasValue ? new(Math.Max(0, value.Value), Note:
            "按当前v0.113.1原生效果、现有牌组与剩余机会估值；有条件触发按折扣先验，不是胜率或已保证战斗收益。") : null;
    }
}
