using System;
using System.Collections.Generic;
using UnityEngine;

namespace MDSAPI.Internal.Interop
{
    /// <summary>
    /// Unity's find and GetComponent calls for a type known only at runtime.
    /// Under IL2CPP these take an <c>Il2CppSystem.Type</c>, not a <see cref="Type"/>.
    /// </summary>
    internal static class UnityFind
    {
#if MDS_IL2CPP
        private static Il2CppSystem.Type T(Type t) => Il2CppInterop.Runtime.Il2CppType.From(t);
#else
        private static Type T(Type t) => t;
#endif

        /// <summary>The first active object of the type in the loaded scenes, or null.</summary>
        internal static object First(Type type)
        {
            if (type is null) return null;
            try
            {
                var o = UnityEngine.Object.FindObjectOfType(T(type));
                return o == null ? null : o;
            }
            catch { return null; }
        }

        /// <summary>Every active object of the type in the loaded scenes.</summary>
        internal static List<object> All(Type type)
        {
            var list = new List<object>();
            if (type is null) return list;
            try
            {
                foreach (var o in UnityEngine.Object.FindObjectsOfType(T(type)))
                    if (o != null) list.Add(o);
            }
            catch { }
            return list;
        }

        /// <summary>Every loaded object of the type, including inactive ones and assets.</summary>
        internal static List<object> Everything(Type type)
        {
            var list = new List<object>();
            if (type is null) return list;
            try
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(T(type)))
                    if (o != null) list.Add(o);
            }
            catch { }
            return list;
        }

        /// <summary>The object as a <see cref="Component"/>, or null.</summary>
        internal static Component AsComponent(object o)
        {
            if (o is null) return null;
            if (o is Component c) return c == null ? null : c;
            return Cast.To(o, typeof(Component)) as Component;
        }

        /// <summary>A component of the type on this object or a parent, or null.</summary>
        internal static object InParent(object component, Type type)
        {
            var c = AsComponent(component);
            if (c == null || type is null) return null;
            try
            {
                var r = c.GetComponentInParent(T(type));
                return r == null ? null : r;
            }
            catch { return null; }
        }

        /// <summary>A component of the type on this object or a child, or null.</summary>
        internal static object InChildren(object component, Type type, bool includeInactive = false)
        {
            var c = AsComponent(component);
            if (c == null || type is null) return null;
            try
            {
                var r = c.GetComponentInChildren(T(type), includeInactive);
                return r == null ? null : r;
            }
            catch { return null; }
        }

        /// <summary>A component of the type on the same object, or null.</summary>
        internal static object On(object component, Type type)
        {
            var c = AsComponent(component);
            if (c == null || type is null) return null;
            try
            {
                var r = c.GetComponent(T(type));
                return r == null ? null : r;
            }
            catch { return null; }
        }

        /// <summary>False for null and for Unity objects that have been destroyed.</summary>
        internal static bool Alive(object o) => o != null && !(o is UnityEngine.Object u && u == null);
    }
}
