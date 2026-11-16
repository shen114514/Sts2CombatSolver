using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CombatSolver.Engine.Common.Mirrors;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using SpireStrategist.Mod;

namespace CombatSolver;

// Explicit developer audit; never runs in ordinary games. This enumerates
// contracts, not effects, and never invokes a native gameplay hook.
internal static class ContentCompatibilityAudit
{
    internal static void Export(string directory, Player player, List<string> checks)
    {
        if (!StandaloneSmokeTest.IsRunning) throw new InvalidOperationException("Developer audit required");
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.DeclaredOnly;
        var errors = new List<string>();
        var registries = new List<(string Source, MethodMirrorRegistryDescriptor Description)>();
        foreach (var type in typeof(Entry).Assembly.GetTypes().Where(t => t.Namespace?.Contains(".Mirrors") == true))
        foreach (var field in type.GetFields(declared).Where(f => f.IsStatic &&
            typeof(IMethodMirrorRegistryDescriptorProvider).IsAssignableFrom(f.FieldType)))
        {
            try
            {
                if (field.GetValue(null) is IMethodMirrorRegistryDescriptorProvider provider)
                    registries.Add(($"{type.FullName}.{field.Name}", provider.DescribeMirrorSupport()));
            }
            catch (Exception e) { errors.Add($"registry:{type.FullName}.{field.Name}:{e.GetBaseException()}"); }
        }
        var bases = new[] { typeof(CardModel), typeof(RelicModel), typeof(EventModel), typeof(EnchantmentModel) };
        var native = typeof(CardModel).Assembly;
        var nativeTypes = native.GetTypes();
        var dataErrors = new List<string>();
        var types = native.GetTypes().Where(t => !t.IsAbstract && !t.IsNested && bases.Any(b => b.IsAssignableFrom(t)))
            .OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
        var models = types.Select(type =>
        {
            string category = bases.First(b => b.IsAssignableFrom(type)).Name;
            var hooks = type.GetMethods(declared).Where(m => m.IsVirtual && !m.IsSpecialName &&
                m.GetBaseDefinition().DeclaringType != type).Select(method =>
                {
                    var matches = registries.Where(r => r.Description.BaseMethod.GetBaseDefinition() == method.GetBaseDefinition()).ToArray();
                    var registration = matches.SelectMany(m => m.Description.Registrations)
                        .FirstOrDefault(r => r.ReceiverType == type);
                    return new { method = method.Name, signature = method.ToString(),
                        coverage = registration?.Kind.ToString() ?? (matches.Any(m => m.Description.StrictInferrer != null || m.Description.Inferrer != null)
                            ? "InferenceAvailableNeedsValidation" : "UnregisteredOrDirectNativeNeedsReview"),
                        registries = matches.Select(m => m.Source).ToArray() };
                }).ToArray();
            object? data = null;
            try
            {
                var canonical = ModelDb.Get(type);
                if (canonical is CardModel card)
                {
                    var mutable = card.ToMutable(); mutable.Owner = player;
                    data = SnapshotReader.ReadCard(mutable);
                }
                else
                {
                    var title = type.GetProperty("Title")?.GetValue(canonical);
                    var description = type.GetProperty("Description")?.GetValue(canonical);
                    data = new { id = canonical.Id.ToString(),
                        title = title is LocString loc ? loc.GetRawText() : title?.ToString(),
                        description = description is LocString text ? text.GetRawText() : description?.ToString() };
                }
            }
            catch (Exception e) { dataErrors.Add($"{type.FullName}:{e.GetBaseException().Message}"); }
            return new { category, type = type.FullName, data,
                fields = type.GetFields(declared).Select(f => new { name = f.Name, type = f.FieldType.FullName,
                    f.IsStatic, f.IsLiteral, constant = f.IsLiteral ? f.GetRawConstantValue()?.ToString() : null }).ToArray(), hooks };
        }).ToArray();
        var enchantments = ModelDb.DebugEnchantments.Where(e => e.GetType().Assembly == native).Select(e =>
            new { id = e.Id.ToString(), type = e.GetType().FullName,
                variables = e.DynamicVars.ToDictionary(v => v.Key, v => v.Value.BaseValue),
                fingerprint = EnchantmentStateSupport.Describe(e) }).ToArray();
        var memberContracts = new List<object>();
        string? source = System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_SOURCE_ROOT");
        if (!string.IsNullOrWhiteSpace(source))
        {
            var pattern = new Regex(
                "typeof\\(\\s*(?<type>[\\w.]+)\\s*\\)\\s*\\.\\s*Get(?<kind>Field|Property)\\s*\\(\\s*\"(?<name>[^\"]+)\"" +
                "|(?:RequiredField|AccessTools\\.Field)\\(\\s*typeof\\(\\s*(?<type>[\\w.]+)\\s*\\)\\s*,\\s*\"(?<name>[^\"]+)\"");
            // The old Glam bug used RequiredField rather than a direct GetField.
            // Prove that this exact lookup shape is covered before scanning sources.
            var obsolete = pattern.Match("RequiredField(typeof(Glam), \"_usedThisCombat\")");
            if (!obsolete.Success || typeof(MegaCrit.Sts2.Core.Models.Enchantments.Glam)
                .GetField(obsolete.Groups["name"].Value, declared) != null)
                throw new InvalidOperationException("Obsolete Glam field preflight regression");
            checks.Add("PreflightRecognizesOriginalRequiredFieldGlamFailure");
            foreach (string file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
            foreach (Match match in pattern.Matches(File.ReadAllText(file)))
            {
                string name = match.Groups["name"].Value, typeName = match.Groups["type"].Value,
                    kind = match.Groups["kind"].Success ? match.Groups["kind"].Value : "Field";
                var matches = nativeTypes.Where(t => t.Name == typeName || t.FullName == typeName).ToArray();
                if (matches.Length != 1) continue; // framework/third-party or ambiguous; no invented result
                Type type = matches[0]; bool exists = false;
                for (Type? owner = type; owner != null; owner = owner.BaseType)
                    exists |= kind == "Field" ? owner.GetField(name, declared) != null : owner.GetProperty(name, declared) != null;
                memberContracts.Add(new { source = Path.GetRelativePath(source, file), type = type.FullName, kind, name, exists });
                if (!exists) errors.Add($"literal_reflection_contract:{type.FullName}.{name}:{file}");
            }
        }
        File.WriteAllText(Path.Combine(directory, "content-audit.json"), JsonSerializer.Serialize(new
        {
            schema = 1, generatedAt = DateTimeOffset.Now,
            gameAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native.Location))),
            scope = "All concrete built-in card/relic/event/enchantment types, including debug/unreachable content. Registration is not differential validation; unregistered may be native read-only or another facade.",
            counts = models.GroupBy(m => m.category).ToDictionary(g => g.Key, g => g.Count()),
            registryCount = registries.Count, registryErrors = errors, dataErrors,
            literalMemberContracts = memberContracts,
            contractScope = "Literal typeof(T).GetField/GetProperty and RequiredField/AccessTools.Field(typeof(T), name). Dynamic names, requested binding flags and third-party types require separate review.",
            enchantments, models
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (errors.Count != 0) throw new InvalidOperationException("Mirror registry initialization failures: " + string.Join("; ", errors));
        checks.Add($"CurrentNativeContentContractsExported:Models={models.Length}:Registries={registries.Count}:Enchantments={enchantments.Length}");
    }
}
