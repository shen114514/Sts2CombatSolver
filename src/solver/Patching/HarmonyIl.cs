using System.Reflection;
using System.Reflection.Emit;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace CombatSolver.Patching;

internal sealed record HarmonyIlMethodBody(IReadOnlyList<CodeInstruction> Instructions);
internal static class HarmonyIl
{
    public static HarmonyIlMethodBody GetOriginalIl(this MethodBase method)
    {
        // Async OnPlay entry methods only construct their state machine. Effects
        // live in MoveNext; inspect that original body without executing the card.
        var stateMachine=method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        MethodBase body=stateMachine==null?method:
            AccessTools.DeclaredMethod(stateMachine,"MoveNext")
                ?? throw new MissingMethodException(stateMachine.FullName,"MoveNext");
        return new(PatchProcessor.GetOriginalInstructions(body));
    }
    public static bool TryGetCalledMethod(CodeInstruction instruction,[NotNullWhen(true)] out MethodInfo? method)
    {
        method=instruction.opcode==OpCodes.Call||instruction.opcode==OpCodes.Callvirt?instruction.operand as MethodInfo:null;
        return method!=null;
    }
    public static bool TryGetLocalLoadIndex(CodeInstruction instruction,out int index)
    {
        var op=instruction.opcode;
        index=op==OpCodes.Ldloc_0?0:op==OpCodes.Ldloc_1?1:op==OpCodes.Ldloc_2?2:op==OpCodes.Ldloc_3?3:
            (op==OpCodes.Ldloc||op==OpCodes.Ldloc_S)?instruction.operand switch {LocalBuilder b=>b.LocalIndex,LocalVariableInfo v=>v.LocalIndex,int i=>i,byte b=>b,_=>-1}:-1;
        return index>=0;
    }
    public static bool LoadsInt32(CodeInstruction instruction,int value)=>instruction.LoadsConstant(value);
}
