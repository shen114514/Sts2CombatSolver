using System.Text;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;

namespace CombatSolver;

internal static class EnchantmentStateSupport
{
    public static void Append(ref StateFingerprintBuilder key, EnchantmentModel? enchantment)
    {
        key.Add(enchantment?.Id.Entry);
        if (enchantment == null)
            return;

        key.Add(enchantment.Amount);
        key.Add((int)enchantment.Status);
        switch (enchantment)
        {
            case Momentum momentum:
                key.Add(momentum._extraDamage);
                break;
        }
    }

    public static void Append(StringBuilder text, EnchantmentModel? enchantment)
    {
        if (enchantment == null)
        {
            text.Append("-:0:0");
            return;
        }

        text.Append(enchantment.Id.Entry)
            .Append(':').Append(enchantment.Amount)
            .Append(':').Append((int)enchantment.Status);
        switch (enchantment)
        {
            case Momentum momentum:
                text.Append(':').Append(momentum._extraDamage);
                break;
        }
    }

    public static string Describe(EnchantmentModel enchantment)
    {
        StringBuilder text = new();
        Append(text, enchantment);
        return text.ToString();
    }

    // Glam's once-per-combat state is Status (already included above) in 0.113.1.
    // Direct publicized access to Momentum makes a removed field a build error,
    // rather than poisoning this entire class, including plain-card fingerprints.
}
