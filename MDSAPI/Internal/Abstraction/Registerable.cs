namespace MDSAPI.Internal.Abstraction
{
    /// <summary>
    /// Something MDSAPI creates and tears down on the mod's behalf. Lifecycle only —
    /// it holds no game handle.
    /// </summary>
    public abstract class Registerable
    {
        internal bool Created { get; private set; }

        internal void CreateInternal()
        {
            if (Created) return;
            Created = true;
            OnCreated();
        }

        internal void DestroyInternal()
        {
            if (!Created) return;
            Created = false;
            OnDestroyed();
        }

        /// <summary>Runs once, after MDSAPI has constructed this object.</summary>
        protected virtual void OnCreated() { }

        /// <summary>
        /// Runs when this object is torn down. Unsubscribe here — a handler left
        /// attached across a save reload fires on a dead instance and stacks up.
        /// </summary>
        protected virtual void OnDestroyed() { }
    }
}
