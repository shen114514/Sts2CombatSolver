using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
namespace CombatSolver;
internal static class ApiCompatHelpers
{
    public static int SmithCount(MegaCrit.Sts2.Core.Entities.RestSite.SmithRestSiteOption option)
        => AccessTools.Field(option.GetType(),"_selection")?.GetValue(option) is System.Collections.ICollection selected?selected.Count:1;
    // Only prediction-owned mutable card instances reach this boundary.
    public static void NotifyTransformed(CardModel target,string name,CardModel counterpart)
    {
        var method=AccessTools.Method(typeof(CardModel),name,[typeof(CardModel)]);
        method?.Invoke(target,[counterpart]);
    }
}
