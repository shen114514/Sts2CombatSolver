using System.Reflection;
using System.Reflection.Emit;
using CombatSolver.Patching;
using HarmonyLib;
void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
bool CallsEffect(string name)=>typeof(Example).GetMethod(name)!.GetOriginalIl().Instructions.Any(i=>HarmonyIl.TryGetCalledMethod(i,out var m)&&m.Name==nameof(Example.Effect));
Check(CallsEffect(nameof(Example.AsyncPlay)),"async card inspection reaches MoveNext effects without executing them");
Check(CallsEffect(nameof(Example.SyncPlay)),"synchronous card inspection still reads original body");
Check(Example.Executions==0,"instruction inspection has no gameplay side effects");
Check(HarmonyIl.LoadsInt32(new(OpCodes.Ldc_I4_0),0),"short integer constant decoding");
Check(HarmonyIl.LoadsInt32(new(OpCodes.Ldc_I4,127),127),"full integer constant decoding");
Check(HarmonyIl.TryGetLocalLoadIndex(new(OpCodes.Ldloc_3),out int local)&&local==3,"local variable index decoding");
Check(!HarmonyIl.TryGetCalledMethod(new(OpCodes.Nop),out _),"non-call instructions cannot be inferred as effects");
Console.WriteLine("7 IL checks passed");
public static class Example
{
 public static int Executions;
 public static void Effect()=>Executions++;
 public static void SyncPlay()=>Effect();
 public static async Task AsyncPlay(){await Task.Yield();Effect();}
}
