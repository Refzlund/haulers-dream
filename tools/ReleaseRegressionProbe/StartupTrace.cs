using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace ReleaseRegressionProbe
{
    internal static class StartupTrace
    {
        internal static void Install()
        {
            var target=AccessTools.Method(typeof(StaticConstructorOnStartupUtility),"CallAll");
            new Harmony("local.regression.startup").Patch(target,transpiler:new HarmonyMethod(typeof(StartupTrace),nameof(Rewrite)));
        }
        static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> code)
        {
            var target=AccessTools.Method(typeof(RuntimeHelpers),nameof(RuntimeHelpers.RunClassConstructor),new[]{typeof(RuntimeTypeHandle)});
            foreach(var c in code){if(c.Calls(target)){c.opcode=OpCodes.Call;c.operand=AccessTools.Method(typeof(StartupTrace),nameof(Run));}yield return c;}
        }
        static void Run(RuntimeTypeHandle handle)
        {
            string name=Type.GetTypeFromHandle(handle).FullName;
            File.AppendAllText(Path.Combine(Probe.Output,"constructors.txt"),"BEGIN "+name+Environment.NewLine);
            RuntimeHelpers.RunClassConstructor(handle);
            File.AppendAllText(Path.Combine(Probe.Output,"constructors.txt"),"END "+name+Environment.NewLine);
        }
    }
}
