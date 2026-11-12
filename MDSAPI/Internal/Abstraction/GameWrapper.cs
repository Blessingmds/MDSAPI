using System;

namespace MDSAPI.Internal.Abstraction
{
    /// <summary>
    /// Base for every MDSAPI type that stands in front of a game object.
    /// <para>
    /// Mods never construct one of these — they get them from a module
    /// (<c>Money.Balance</c>, <c>Npcs.Find(id)</c>). The native handle stays internal
    /// because a game type crossing the public API defeats the whole point of MDSAPI.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The underlying game type.</typeparam>
    public abstract class GameWrapper<T> : IEquatable<GameWrapper<T>> where T : class
    {
        internal T Native { get; }

        internal GameWrapper(T native) =>
            Native = native ?? throw new ArgumentNullException(nameof(native));

        /// <summary>
        /// False once the underlying game object is gone — despawned, scene-unloaded, or
        /// destroyed by a save reload. Check this before acting on a wrapper you have
        /// held for any length of time; NPCs and UI objects do not outlive a load.
        /// </summary>
        public bool IsValid
        {
            get
            {
                if (Native is null) return false;
                // Unity overloads == so a destroyed object compares equal to null.
                if (Native is UnityEngine.Object uo) return uo != null;
#if MDS_IL2CPP
                // Under IL2CPP a collected object throws rather than reporting itself dead.
                try { _ = Native.ToString(); } catch { return false; }
#endif
                return true;
            }
        }

        /// <summary>Throws a message worth reading if the underlying object has gone.</summary>
        protected void RequireValid()
        {
            if (!IsValid)
                throw new InvalidOperationException(
                    $"{GetType().Name} refers to a game object that no longer exists. " +
                    "Wrappers do not survive a save reload or a scene change — " +
                    "fetch a fresh one from its module instead of caching this.");
        }

        // Identity follows the game object, so two wrappers around the same NPC are equal.
        public bool Equals(GameWrapper<T> other) => other is not null && ReferenceEquals(Native, other.Native);
        public override bool Equals(object obj) => Equals(obj as GameWrapper<T>);
        public override int GetHashCode() => Native?.GetHashCode() ?? 0;
    }
}
