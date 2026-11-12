using System;
using MDSAPI.Internal.Interop;
using UnityEngine.Events;

namespace MDSAPI.Unity
{
    /// <summary>
    /// The handful of Unity calls that are spelled differently on the two runtimes, spelled
    /// once. With these, UI code compiles unchanged for both branches.
    /// </summary>
    public static class Cross
    {
        /// <summary>True on the default (IL2CPP) branch, false on the alternate (Mono) one.</summary>
#if MDS_IL2CPP
        public const bool IsIl2Cpp = true;
#else
        public const bool IsIl2Cpp = false;
#endif

        /// <summary>
        /// The object's real class name — "DryingRack", not the wrapper's "BuildableItem".
        /// Replaces <c>GetIl2CppType().Name</c>, which does not exist under Mono.
        /// </summary>
        public static string TypeName(object obj) => Cast.TypeName(obj);

        /// <summary>
        /// An identity for a game object that is stable across reads, for dictionary keys and
        /// "is this the same thing as last frame" checks. Replaces <c>.Pointer</c>.
        /// </summary>
        public static ObjectKey Key(object obj) => ObjectKey.Of(obj);

        /// <summary>Adds a listener to a UnityEvent (a button's onClick, a toggle's change...).</summary>
        public static void Listen(UnityEvent unityEvent, Action action)
        {
            if (unityEvent is null || action is null) return;
#if MDS_IL2CPP
            unityEvent.AddListener((UnityAction)action);
#else
            unityEvent.AddListener(new UnityAction(action));
#endif
        }

        /// <summary>Adds a listener to a UnityEvent that carries a value (an input field's text...).</summary>
        public static void Listen<T>(UnityEvent<T> unityEvent, Action<T> action)
        {
            if (unityEvent is null || action is null) return;
#if MDS_IL2CPP
            unityEvent.AddListener((UnityAction<T>)action);
#else
            unityEvent.AddListener(new UnityAction<T>(action));
#endif
        }

        /// <summary>Runs <paramref name="action"/> when the button is clicked.</summary>
        public static void OnClick(UnityEngine.UI.Button button, Action action)
        {
            if (button != null) Listen(button.onClick, action);
        }
    }

    /// <summary>Extension forms of <see cref="Cross"/>.</summary>
    public static class CrossExtensions
    {
        /// <summary>
        /// The object viewed as <typeparamref name="T"/>, or null — a placeholder
        /// <c>Graphic</c> as the <c>TextMeshProUGUI</c> it really is. Replaces IL2CPP's
        /// <c>.Cast&lt;T&gt;()</c> / <c>.TryCast&lt;T&gt;()</c>, which do not exist under Mono, and
        /// a plain <c>as</c>, which fails under IL2CPP on a base-typed wrapper.
        /// </summary>
        public static T As<T>(this UnityEngine.Object obj) where T : class
        {
            if (obj == null) return null;
            return obj as T ?? Cast.To(obj, typeof(T)) as T;
        }
    }

    /// <summary>
    /// Identity of a game object. Two reads of the same object give equal keys on both
    /// runtimes — which a plain reference does not under IL2CPP, where every read can hand
    /// back a fresh wrapper around the same native object.
    /// </summary>
    public readonly struct ObjectKey : IEquatable<ObjectKey>
    {
        private readonly IntPtr _pointer;
        private readonly object _reference;

        private ObjectKey(IntPtr pointer, object reference) { _pointer = pointer; _reference = reference; }

        /// <summary>The key of <paramref name="obj"/>; <see cref="None"/> for null.</summary>
        public static ObjectKey Of(object obj)
        {
            if (obj is null) return default;
#if MDS_IL2CPP
            if (obj is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase b)
                return new ObjectKey(b.Pointer, null);
#endif
            return new ObjectKey(IntPtr.Zero, obj);
        }

        /// <summary>The key of nothing.</summary>
        public static ObjectKey None => default;

        /// <summary>False for the key of nothing.</summary>
        public bool IsSet => _pointer != IntPtr.Zero || _reference != null;

        /// <inheritdoc/>
        public bool Equals(ObjectKey other) =>
            _pointer == other._pointer && ReferenceEquals(_reference, other._reference);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ObjectKey k && Equals(k);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            _pointer != IntPtr.Zero ? _pointer.GetHashCode()
            : _reference != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_reference)
            : 0;

        /// <summary>Same object.</summary>
        public static bool operator ==(ObjectKey a, ObjectKey b) => a.Equals(b);

        /// <summary>Different objects.</summary>
        public static bool operator !=(ObjectKey a, ObjectKey b) => !a.Equals(b);
    }
}
