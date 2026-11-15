using System.Reflection;
using HarmonyLib;

namespace CombatSolver.Patching;

internal interface IPatchMethod { }
internal sealed record ModPatchTarget(Type DeclaringType, string MethodName, Type[] ArgumentTypes);

// Only this assembly's explicitly registered patches are installed. On a required
// failure, remove this owner's partial patches and leave native player control.
internal sealed class SolverPatcher(string owner)
{
    private readonly List<Type> _patches = [];
    public void RegisterPatch<T>() where T:IPatchMethod => _patches.Add(typeof(T));
    public void Apply(Action disable)
    {
        var harmony=new Harmony(owner);
        try
        {
            foreach(var type in _patches)
            {
                var targets=(ModPatchTarget[])type.GetMethod("GetTargets")!.Invoke(null,null)!;
                foreach(var target in targets)
                {
                    var original=AccessTools.DeclaredMethod(target.DeclaringType,target.MethodName,target.ArgumentTypes)
                        ?? throw new MissingMethodException(target.DeclaringType.FullName,target.MethodName);
                    HarmonyMethod? Patch(string name)
                    {
                        var method=AccessTools.DeclaredMethod(type,name);
                        return method==null?null:new HarmonyMethod(method);
                    }
                    harmony.Patch(original,Patch("Prefix"),Patch("Postfix"),Patch("Transpiler"),Patch("Finalizer"));
                }
                Entry.Logger.Info($"[patch] installed={type.Name} targets={targets.Length}");
            }
        }
        catch(Exception ex)
        {
            harmony.UnpatchAll(owner);
            disable();
            Entry.Logger.Error("[patch] required_patch_failed native_control=true "+ex);
        }
    }
}
