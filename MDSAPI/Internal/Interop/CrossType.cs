using System;

namespace MDSAPI.Internal.Interop
{
    /// <summary>
    /// Type tests and casts that behave the same on both runtimes.
    /// Under IL2CPP, <c>is</c> and <c>(T)</c> do not work on game objects — they are
    /// interop wrappers around native pointers, so the managed cast has nothing to test.
    /// </summary>
    internal static class CrossType
    {
        /// <summary>Casts, returning null when the object is not of that type.</summary>
        internal static T As<T>(object obj) where T : class
            => obj is null ? null : Caster<T>.Cast(obj);

        /// <summary>True when the object is of that type.</summary>
        internal static bool Is<T>(object obj) where T : class => As<T>(obj) != null;

        /// <summary>
        /// The runtime <c>Type</c> a game API expects. Under IL2CPP a managed
        /// <c>typeof(T)</c> is the wrong object to hand to a game method that wants a type.
        /// </summary>
        internal static object TypeOf<T>() where T : class
        {
#if MDS_IL2CPP
            return Il2CppInterop.Runtime.Il2CppType.Of<T>();
#else
            return typeof(T);
#endif
        }

        /// <summary>
        /// Builds the cast for one <typeparamref name="T"/>, once.
        /// <para>
        /// Il2CppInterop's <c>TryCast&lt;T&gt;</c> is constrained to
        /// <c>Il2CppObjectBase</c>, which a runtime-neutral helper cannot promise — the
        /// Mono build has no such base type. Rather than let the public signature differ
        /// between runtimes, the right cast is resolved reflectively the first time a
        /// given T is used and cached in this static generic, so the per-call cost is a
        /// delegate invoke rather than a reflection lookup.
        /// </para>
        /// </summary>
        private static class Caster<T> where T : class
        {
            internal static readonly Func<object, T> Cast = Build();

            private static Func<object, T> Build()
            {
#if MDS_IL2CPP
                var il2CppBase = typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase);
                if (il2CppBase.IsAssignableFrom(typeof(T)))
                {
                    var tryCast = il2CppBase
                        .GetMethod(nameof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase.TryCast))
                        ?.MakeGenericMethod(typeof(T));

                    if (tryCast != null)
                        return obj => obj is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase b
                            ? (T)tryCast.Invoke(b, null)
                            : obj as T;
                }
#endif
                return obj => obj as T;
            }
        }
    }
}
