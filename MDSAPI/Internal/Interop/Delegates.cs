using System;
using System.Linq;
using System.Reflection;

namespace MDSAPI.Internal.Interop
{
    /// <summary>
    /// Builds the handler object a game event expects, on either runtime.
    /// </summary>
    internal static class Delegates
    {
        private static MethodInfo _convertDelegate;
        private static bool _looked;

        /// <summary>
        /// Converts a managed <see cref="Action"/> into whatever
        /// <paramref name="parameterType"/> is, or null when that is not possible.
        /// <para>
        /// On Mono a UnityEvent listener is an ordinary delegate and
        /// <c>Delegate.CreateDelegate</c> is all that is needed. On IL2CPP the same
        /// listener is an <c>Il2CppSystem.Action</c> — an interop object wrapping a native
        /// pointer, not a <see cref="Delegate"/> — so CreateDelegate throws
        /// "Type must derive from Delegate" and Il2CppInterop has to do the conversion.
        /// </para>
        /// <para>
        /// Il2CppInterop is reached reflectively rather than referenced, so the Mono build
        /// does not link against an assembly that only exists on the other branch, and this
        /// file needs no <c>#if</c>.
        /// </para>
        /// </summary>
        internal static object CreateHandlerFor(Type parameterType, Action handler)
        {
            if (parameterType is null || handler is null) return null;

            if (typeof(Delegate).IsAssignableFrom(parameterType))
                return Delegate.CreateDelegate(parameterType, handler.Target, handler.Method);

            var convert = ConvertDelegateMethod();
            if (convert is null) return null;

            try { return convert.MakeGenericMethod(parameterType).Invoke(null, new object[] { handler }); }
            catch { return null; }
        }

        /// <summary>
        /// Converts any managed delegate into <paramref name="delegateType"/>: an ordinary
        /// delegate type on Mono, an interop delegate under IL2CPP. The managed delegate's
        /// signature must match the target's exactly — Il2CppInterop marshals arguments by
        /// the managed signature. Null when it cannot be done.
        /// </summary>
        internal static object ConvertTo(Type delegateType, Delegate handler)
        {
            if (delegateType is null || handler is null) return null;

            if (typeof(Delegate).IsAssignableFrom(delegateType))
            {
                try { return Delegate.CreateDelegate(delegateType, handler.Target, handler.Method); }
                catch { return null; }
            }

            var convert = ConvertDelegateMethod();
            if (convert is null) return null;
            try { return convert.MakeGenericMethod(delegateType).Invoke(null, new object[] { handler }); }
            catch { return null; }
        }

        private static MethodInfo ConvertDelegateMethod()
        {
            if (_looked) return _convertDelegate;
            _looked = true;

            var support = Type.GetType("Il2CppInterop.Runtime.DelegateSupport, Il2CppInterop.Runtime");
            _convertDelegate = support?
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "ConvertDelegate"
                                     && m.IsGenericMethodDefinition
                                     && m.GetGenericArguments().Length == 1
                                     && m.GetParameters().Length == 1);
            return _convertDelegate;
        }
    }
}
