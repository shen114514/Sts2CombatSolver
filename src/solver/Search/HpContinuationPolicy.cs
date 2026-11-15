using System.Globalization;
using System.Text.RegularExpressions;

namespace CombatSolver;

internal static class HpContinuationPolicy
{
    // Enemy HP may only be lower than predicted. Player HP remains exact because both
    // survival and conditional card effects depend on it. Never reuse an optimistic route.
    internal static bool TryGetSafeDrift(string difference, bool enemy, out int drift)
    {
        drift = 0;
        Match match = Regex.Match(difference, @"expected=\{(-?\d+)\} actual=\{(-?\d+)\}$");
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int expected)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int actual)
            || expected < 0 || actual < 0)
            return false;
        if (!enemy)
            return actual == expected;
        if (actual > expected)
            return false;
        drift = expected - actual;
        return true;
    }
}
