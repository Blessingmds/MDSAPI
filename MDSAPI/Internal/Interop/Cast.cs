using System;
using System.Collections.Generic;
using System.Reflection;

namespace MDSAPI.Internal.Interop
{
    /// <summary>
    /// Runtime type tests and casts against a game type known only by <see cref="Type"/>.
    /// <para>
    /// Under IL2CPP a game object reached through a base-typed member is wrapped as that
    /// base type: an <c>ItemSlot.SlotOwner</c> that is really a drying rack arrives as an
    /// <c>IItemSlotOwner</c> wrapper, and a managed <c>is DryingRack</c> says no. The native
    /// object has to be asked, which is what <c>TryCast</c> does. Under Mono an ordinary
    /// type test is the whole story.
    /// </para>
    /// </summary>
    internal static class Cast
    {
        private static readonly Dictionary<Type, Func<object, object>> Casters = new();

        /// <summary><paramref name="obj"/> viewed as <paramref name="type"/>, or null.</summary>
        internal static object To(object obj, Type type)
        {
            if (obj is null || type is null) return null;
            if (type.IsInstanceOfType(obj)) return obj;
#if MDS_IL2CPP
            return CasterFor(type)(obj);
#else
            return null;
#endif
        }

        /// <summary>True when <paramref name="obj"/> is a <paramref name="type"/>.</summary>
        internal static bool Is(object obj, Type type) => To(obj, type) != null;

        /// <summary>
        /// A delegate that casts to <paramref name="type"/>; used inside compiled accessors so
        /// the per-call cost is one delegate invoke.
        /// </summary>
        internal static Func<object, object> CasterFor(Type type)
        {
            if (Casters.TryGetValue(type, out var c)) return c;
#if MDS_IL2CPP
            var baseType = typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase);
            if (baseType.IsAssignableFrom(type))
            {
                var m = typeof(Cast).GetMethod(nameof(Il2CppCast), BindingFlags.NonPublic | BindingFlags.Static)
                                    .MakeGenericMethod(type);
                c = (Func<object, object>)Delegate.CreateDelegate(typeof(Func<object, object>), m);
            }
            else c = o => type.IsInstanceOfType(o) ? o : null;
#else
            c = o => type.IsInstanceOfType(o) ? o : null;
#endif
            Casters[type] = c;
            return c;
        }

#if MDS_IL2CPP
        private static object Il2CppCast<T>(object o) where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
        {
            if (o is T t) return t;
            if (o is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase b)
            {
                try { return b.TryCast<T>(); } catch { return null; }
            }
            return null;
        }
#endif

        /// <summary>The full native class name ("ScheduleOne.UI.MainMenu.MenuScreen") on either runtime.</summary>
        internal static string FullTypeName(object obj)
        {
            if (obj is null) return null;
#if MDS_IL2CPP
            if (obj is Il2CppSystem.Object o)
            {
                try { return o.GetIl2CppType().FullName; } catch { }
            }
#endif
            return obj.GetType().FullName;
        }

        /// <summary>The native class name under IL2CPP, the CLR name under Mono.</summary>
        internal static string TypeName(object obj)
        {
            if (obj is null) return null;
#if MDS_IL2CPP
            if (obj is Il2CppSystem.Object o)
            {
                try { return o.GetIl2CppType().Name; } catch { }
            }
#endif
            return obj.GetType().Name;
        }
    }
}
