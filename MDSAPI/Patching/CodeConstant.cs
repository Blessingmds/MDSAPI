using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MDSAPI.Internal.Interop;
using MDSAPI.Logging;
#if MDS_IL2CPP
using System.Diagnostics;
using System.Runtime.InteropServices;
#else
using System.Reflection.Emit;
using HarmonyLib;
#endif

namespace MDSAPI.Patching
{
    /// <summary>
    /// Overrides a number the game has compiled in as a C# <c>const</c> — the cash a slot
    /// holds, the ATM's weekly deposit limit — inside named methods.
    /// <para>
    /// A const is not a field at runtime: the compiler copies its value into every method
    /// that uses it, so setting the "field" does nothing. Under Mono those copies are IL
    /// <c>ldc.r4</c> instructions, rewritten here by a Harmony transpiler to read a value
    /// the mod controls. Under IL2CPP they are loads of a shared float from read-only data
    /// in the native code; each method's load is repointed to a float the mod controls.
    /// </para>
    /// <para>
    /// Either way, nothing is changed unless every listed method can be matched — a partial
    /// override (a limit enforced in one place and not another) is worse than none.
    /// </para>
    /// </summary>
    public sealed class CodeConstant
    {
        private readonly string _label;

        /// <summary>The game's own value.</summary>
        public float Vanilla { get; }

        /// <summary>True once installed and in effect; false if this game version did not match.</summary>
        public bool IsAvailable { get; private set; }

        private static readonly Dictionary<string, CodeConstant> Installed = new();

        private CodeConstant(string label, float vanilla) { _label = label; Vanilla = vanilla; }

        /// <summary>
        /// Installs the override. <paramref name="relativeType"/> is the game type whose
        /// <paramref name="methods"/> use the constant. Call once, from OnInitializeMelon.
        /// </summary>
        /// <param name="label">What the player would call it, for the log ("Cash per slot").</param>
        /// <param name="relativeType">Game type, relative to the game root.</param>
        /// <param name="vanilla">The constant's value in the game.</param>
        /// <param name="methods">Methods of that type that use it.</param>
        public static CodeConstant Override(string label, string relativeType, float vanilla, params string[] methods)
        {
            // The same constant can only be rewritten once: the second scan would find the
            // first one's work, not the game's value. A second mod asking for it gets the same
            // override — the value is shared, and the last one to Set it wins.
            var key = relativeType + "|" + vanilla + "|" + string.Join(",", methods);
            if (Installed.TryGetValue(key, out var existing))
            {
                Log.Info(label + " is already overridden by another mod; sharing it (the last value set wins).");
                return existing;
            }

            var c = new CodeConstant(label, vanilla);
            Installed[key] = c;
            var type = GameTypes.Find(relativeType);
            if (type is null)
            {
                Log.Warn(label + " is unavailable in this game version (type " + relativeType + " not found).");
                return c;
            }
            try { c.IsAvailable = c.Install(type, methods); }
            catch (Exception ex) { Log.Error(label + " is unavailable.", ex); }
            Patch.Record("constant: " + label, c.IsAvailable);
            return c;
        }

        /// <summary>Sets the value the game sees; null puts the game's own value back.</summary>
        public void Set(float? value)
        {
            if (IsAvailable) Write(value ?? Vanilla);
        }

#if MDS_IL2CPP
        private unsafe float* _value;

        private unsafe void Write(float v) { if (_value != null) *_value = v; }

        private unsafe bool Install(Type il2cppType, string[] methods)
        {
            var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                                .FirstOrDefault(m => m.ModuleName == "GameAssembly.dll");
            if (module is null) { Log.Warn(_label + " is unavailable: GameAssembly.dll not found."); return false; }
            long lo = (long)module.BaseAddress, hi = lo + module.ModuleMemorySize;

            // The next method of the class bounds each scan, so it cannot run into unrelated code.
            var pointers = il2cppType.GetFields(BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => f.Name.StartsWith("NativeMethodInfoPtr_", StringComparison.Ordinal))
                .ToDictionary(f => f.Name, f => *(long*)(IntPtr)f.GetValue(null));
            var sorted = pointers.Values.Where(p => p != 0).OrderBy(p => p).ToArray();

            var sites = new List<(string Name, long Site)>();
            foreach (var name in methods)
            {
                long start = pointers.FirstOrDefault(kv => kv.Key.StartsWith("NativeMethodInfoPtr_" + name + "_", StringComparison.Ordinal)).Value;
                long site = 0;
                if (start >= lo && start < hi)
                {
                    long end = Math.Min(sorted.FirstOrDefault(p => p > start, start + 0x4000), hi);
                    var code = new byte[end - start];
                    Marshal.Copy((IntPtr)start, code, 0, code.Length);
                    var hits = RipFloatScan.Find(code, start, lo, hi, a => *(float*)a, Vanilla);
                    if (hits.Count == 1) site = start + hits[0];
                }
                sites.Add((name, site));
            }

            var missing = sites.Where(s => s.Site == 0).Select(s => s.Name).ToArray();
            if (missing.Length > 0)
            {
                Log.Warn(_label + " is unavailable in this game version (no match in " + string.Join(", ", missing) + ").");
                return false;
            }

            var value = (float*)AllocNear(lo);
            if (value == null || sites.Any(s => !FitsInt32((long)value - (s.Site + 4))))
            {
                Log.Warn(_label + " is unavailable: no memory near the game code.");
                return false;
            }
            *value = Vanilla;
            foreach (var (_, site) in sites) WriteInt32(site, (int)((long)value - (site + 4)));
            _value = value;
            return true;
        }

        private static bool FitsInt32(long v) => v >= int.MinValue && v <= int.MaxValue;

        private static unsafe void WriteInt32(long address, int v)
        {
            VirtualProtect((IntPtr)address, (UIntPtr)4, PageExecuteReadWrite, out var old);
            *(int*)address = v;
            VirtualProtect((IntPtr)address, (UIntPtr)4, old, out _);
            FlushInstructionCache(GetCurrentProcess(), (IntPtr)address, (UIntPtr)4);
        }

        // rip-relative operands reach +-2 GB, so the float has to live close to GameAssembly
        private static IntPtr AllocNear(long target)
        {
            const long step = 0x10000;
            for (long d = step; d < 0x7000_0000; d += step)
                foreach (var a in new[] { target - d, target + d })
                {
                    var p = VirtualAlloc((IntPtr)(a & ~(step - 1)), (UIntPtr)0x1000, MemCommit | MemReserve, PageReadWrite);
                    if (p != IntPtr.Zero) return p;
                }
            return IntPtr.Zero;
        }

        private const uint MemCommit = 0x1000, MemReserve = 0x2000, PageReadWrite = 0x04, PageExecuteReadWrite = 0x40;
        [DllImport("kernel32")] private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protect);
        [DllImport("kernel32")] private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protect, out uint old);
        [DllImport("kernel32")] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
        [DllImport("kernel32")] private static extern IntPtr GetCurrentProcess();
#else
        // Rewritten IL reads Values[index]; the array is replaced, never resized in place,
        // so a method running mid-registration always sees a whole array.
        internal static float[] Values = new float[0];
        private static readonly Dictionary<MethodBase, List<(float Vanilla, int Index)>> Pending = new();
        private static readonly Dictionary<MethodBase, int> Rewritten = new();
        private int _index = -1;

        private void Write(float v) { if (_index >= 0) Values[_index] = v; }

        private bool Install(Type type, string[] methods)
        {
            const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                   BindingFlags.Static | BindingFlags.DeclaredOnly;
            var targets = new List<MethodInfo>();
            foreach (var name in methods)
            {
                var m = type.GetMethods(F).FirstOrDefault(x => x.Name == name);
                if (m is null)
                {
                    Log.Warn(_label + " is unavailable in this game version (" + type.Name + "." + name + " not found).");
                    return false;
                }
                targets.Add(m);
            }

            var next = new float[Values.Length + 1];
            Values.CopyTo(next, 0);
            _index = Values.Length;
            next[_index] = Vanilla;
            Values = next;

            var transpiler = typeof(CodeConstant).GetMethod(nameof(Transpiler), BindingFlags.NonPublic | BindingFlags.Static);
            var total = 0;
            foreach (var m in targets)
            {
                if (!Pending.TryGetValue(m, out var list)) Pending[m] = list = new List<(float, int)>();
                list.Add((Vanilla, _index));
                Rewritten[m] = 0;
                if (!Hooks.Transpile(_label + " in " + type.Name + "." + m.Name, m, transpiler)) return false;
                total += Rewritten[m];
            }

            // Mono keeps every use as its own instruction, so a method can hold several (ATM.Update
            // uses the limit three times) or none (where the native build inlined a getter).
            // What matters is that the value is reached at all.
            if (total == 0)
            {
                Log.Warn(_label + " is unavailable in this game version (the value " + Vanilla +
                         " was not found in " + string.Join(", ", methods) + ").");
                _index = -1;
                return false;
            }
            return true;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var field = typeof(CodeConstant).GetField(nameof(Values), BindingFlags.NonPublic | BindingFlags.Static);
            Pending.TryGetValue(__originalMethod, out var wanted);
            var count = 0;
            foreach (var ins in instructions)
            {
                if (wanted != null && ins.opcode == OpCodes.Ldc_R4 && ins.operand is float f)
                {
                    var match = wanted.FindIndex(w => w.Vanilla == f);
                    if (match >= 0)
                    {
                        count++;
                        // Rewrite in place so jump labels and exception blocks on the original
                        // instruction carry over. Copying them into a new instruction would name
                        // Harmony's Label type, which the net35 Harmony and netstandard disagree on.
                        ins.opcode = OpCodes.Ldsfld;
                        ins.operand = field;
                        yield return ins;
                        yield return new CodeInstruction(OpCodes.Ldc_I4, wanted[match].Index);
                        yield return new CodeInstruction(OpCodes.Ldelem_R4);
                        continue;
                    }
                }
                yield return ins;
            }
            if (Rewritten.ContainsKey(__originalMethod)) Rewritten[__originalMethod] = count;
        }
#endif
    }

    /// <summary>x64 matching for <see cref="CodeConstant"/> under IL2CPP.</summary>
    internal static class RipFloatScan
    {
        // SSE scalar ops that read a float: movss, addss, mulss, subss, minss, divss, maxss
        private static readonly HashSet<byte> Ops = new() { 0x10, 0x58, 0x59, 0x5C, 0x5D, 0x5E, 0x5F };

        /// <summary>
        /// Offsets of the disp32 of every <c>op xmm, [rip+disp32]</c> whose target holds
        /// <paramref name="value"/>. Targets outside [lo, hi) are never read.
        /// </summary>
        internal static List<int> Find(byte[] code, long codeAddress, long lo, long hi, Func<long, float> read, float value)
        {
            var hits = new List<int>();
            for (int i = 0; i < code.Length; i++)
            {
                if (code[i] != 0xF3) continue;
                int j = i + 1;
                if (j < code.Length && (code[j] & 0xF0) == 0x40) j++; // REX for xmm8-15
                if (j + 7 > code.Length || code[j] != 0x0F || !Ops.Contains(code[j + 1]) || (code[j + 2] & 0xC7) != 0x05) continue;
                long target = codeAddress + j + 7 + BitConverter.ToInt32(code, j + 3);
                if (target >= lo && target + 4 <= hi && read(target) == value) hits.Add(j + 3);
            }
            return hits;
        }
    }
}
