using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace MDSAPI.Internal.Interop
{
    /// <summary>
    /// Reads the elements of a game collection on either runtime.
    /// <para>
    /// Under Mono a game <c>List&lt;T&gt;</c> is a System list. Under IL2CPP it is an
    /// <c>Il2CppSystem</c> list that does not implement <see cref="IEnumerable"/> at all —
    /// only <c>Count</c> and an indexer — while game arrays are interop arrays that do.
    /// This takes whichever shape it is handed.
    /// </para>
    /// </summary>
    internal static class Seq
    {
        private sealed class Shape
        {
            internal PropertyInfo Count;
            internal MethodInfo Item;
        }

        private static readonly Dictionary<Type, Shape> Shapes = new();

        /// <summary>The elements, in order; empty for null or an unknown shape.</summary>
        internal static List<object> Items(object collection)
        {
            var result = new List<object>();
            if (collection is null || collection is string) return result;

            if (collection is IEnumerable e)
            {
                foreach (var x in e) result.Add(x);
                return result;
            }

            var shape = ShapeOf(collection.GetType());
            if (shape is null) return result;
            var n = (int)shape.Count.GetValue(collection);
            var arg = new object[1];
            for (var i = 0; i < n; i++)
            {
                arg[0] = i;
                result.Add(shape.Item.Invoke(collection, arg));
            }
            return result;
        }

        /// <summary>The number of elements; 0 for null or an unknown shape.</summary>
        internal static int Count(object collection)
        {
            if (collection is null) return 0;
            if (collection is ICollection c) return c.Count;
            var shape = ShapeOf(collection.GetType());
            return shape is null ? Items(collection).Count : (int)shape.Count.GetValue(collection);
        }

        /// <summary>The element at <paramref name="index"/>, or null.</summary>
        internal static object At(object collection, int index)
        {
            if (collection is null || index < 0) return null;
            if (collection is IList l) return index < l.Count ? l[index] : null;
            var shape = ShapeOf(collection.GetType());
            if (shape is null)
            {
                var all = Items(collection);
                return index < all.Count ? all[index] : null;
            }
            return index < (int)shape.Count.GetValue(collection) ? shape.Item.Invoke(collection, new object[] { index }) : null;
        }

        private static Shape ShapeOf(Type t)
        {
            if (Shapes.TryGetValue(t, out var s)) return s;
            const BindingFlags F = BindingFlags.Public | BindingFlags.Instance;
            var count = t.GetProperty("Count", F) ?? t.GetProperty("Length", F);
            var item = t.GetMethod("get_Item", F, null, new[] { typeof(int) }, null);
            s = count != null && item != null ? new Shape { Count = count, Item = item } : null;
            Shapes[t] = s;
            return s;
        }

        /// <summary>
        /// Text of a value. Under IL2CPP an interop struct such as <c>Il2CppSystem.Guid</c>
        /// declares its own ToString, which must be the one called; calling
        /// <see cref="object.ToString"/> on the wrapper prints the wrapper's type name.
        /// </summary>
        internal static string Text(object value)
        {
            if (value is null) return null;
            if (value is string s) return s;
            try
            {
                var m = value.GetType().GetMethod("ToString", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null)
                     ?? value.GetType().GetMethod("ToString", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                return m != null ? m.Invoke(value, null) as string : value.ToString();
            }
            catch { return value.ToString(); }
        }
    }
}
