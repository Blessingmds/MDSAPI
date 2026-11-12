using System;
using System.Linq;
using System.Reflection;
using MDSAPI.Logging;

namespace MDSAPI.Internal.Interop
{
    /// <summary>
    /// How MDSAPI names a game member: by a list of candidates rather than one string.
    /// <para>
    /// A game update that renames a member is the single most common way mods die. Binding
    /// to one name means that rename reaches every mod. Binding to an ordered list means
    /// it usually reaches nobody, and when it does reach someone it does so loudly, in one
    /// place, with a log line naming what moved.
    /// </para>
    /// <para>
    /// Shape is discovered rather than declared, because a member can be a property on one
    /// runtime and a field on the other — measured, not theoretical: <c>onPreLoad</c> is a
    /// field under Mono and a property under IL2CPP at game 0.4.7f12.
    /// </para>
    /// <para>See ROBUSTNESS.md for the contract this implements.</para>
    /// </summary>
    internal sealed class MemberSpec
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Instance | BindingFlags.Static;

        private readonly string _typeName;
        private readonly string[] _candidates;

        private bool _resolved;
        private PropertyInfo _property;
        private FieldInfo _field;
        private MethodInfo[] _methods;

        /// <summary>The candidate that actually matched, or null if none did.</summary>
        internal string BoundTo { get; private set; }

        /// <summary>True once a candidate has matched.</summary>
        internal bool IsAvailable { get { Resolve(); return BoundTo != null; } }

        /// <param name="typeName">Game type, relative to the root, e.g. "Money.MoneyManager".</param>
        /// <param name="candidates">Member names, current first, then older or likely ones.</param>
        internal MemberSpec(string typeName, params string[] candidates)
        {
            _typeName = typeName;
            _candidates = candidates;
        }

        private object Owner() => Singletons.Resolve(_typeName);

        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            var owner = Owner();
            if (owner is null) return;
            var type = owner.GetType();

            foreach (var name in _candidates)
            {
                var prop = type.GetProperty(name, Flags);
                if (prop != null) { _property = prop; Bound(name); return; }

                var field = type.GetField(name, Flags);
                if (field != null) { _field = field; Bound(name); return; }

                var methods = type.GetMethods(Flags).Where(m => m.Name == name).ToArray();
                if (methods.Length > 0) { _methods = methods; Bound(name); return; }
            }

            Log.Warn("Could not find " + _typeName + "." + _candidates[0] +
                     " (tried " + string.Join(", ", _candidates) + "). " +
                     "Whatever uses it is unavailable this session.");
        }

        private void Bound(string name)
        {
            BoundTo = name;

            // Say so when a fallback matched. A wrapper quietly running on candidate #3 for
            // six months and then breaking is worse than failing on day one, because by
            // then nobody remembers the rename happened.
            if (name != _candidates[0])
                Log.Warn(_typeName + "." + _candidates[0] + " was not found; using '" +
                         name + "' instead. The game has likely been updated — this still " +
                         "works, but it is worth reporting.");
        }

        /// <summary>Reads the member. Returns <paramref name="fallback"/> if unavailable.</summary>
        internal T Get<T>(T fallback = default)
        {
            Resolve();
            if (BoundTo is null) return fallback;

            var owner = Owner();
            if (owner is null) return fallback;

            try
            {
                object value =
                    _property != null ? _property.GetValue(owner) :
                    _field != null ? _field.GetValue(owner) :
                    Invoke();
                return value is T typed ? typed : fallback;
            }
            catch (Exception ex)
            {
                Log.Error("Reading " + _typeName + "." + BoundTo + " threw.", ex);
                return fallback;
            }
        }

        /// <summary>Calls the member and returns its result, or <paramref name="fallback"/>.</summary>
        internal T Call<T>(T fallback, params object[] args)
        {
            Resolve();
            if (BoundTo is null || _methods is null) return fallback;

            var owner = Owner();
            if (owner is null) return fallback;

            var method = Choose(args?.Length ?? 0);
            if (method is null) return fallback;

            try
            {
                var value = method.Invoke(owner, Pad(method, args));
                return value is T typed ? typed : fallback;
            }
            catch (Exception ex)
            {
                Log.Error("Calling " + _typeName + "." + BoundTo + " threw.", ex);
                return fallback;
            }
        }

        /// <summary>Calls the member. Returns false, having logged, if it could not.</summary>
        internal bool Invoke(params object[] args)
        {
            Resolve();
            if (BoundTo is null || _methods is null) return false;

            var owner = Owner();
            if (owner is null) return false;

            var method = Choose(args?.Length ?? 0);
            if (method is null)
            {
                Log.Error(_typeName + "." + BoundTo + " exists but takes no overload " +
                          "matching " + (args?.Length ?? 0) + " argument(s).");
                return false;
            }

            try { method.Invoke(owner, Pad(method, args)); return true; }
            catch (Exception ex)
            {
                Log.Error("Calling " + _typeName + "." + BoundTo + " threw.", ex);
                return false;
            }
        }

        private object Invoke()
        {
            var method = Choose(0);
            return method?.Invoke(Owner(), null);
        }

        /// <summary>
        /// Exact arity if there is one, otherwise the smallest overload that can take what
        /// we have. An update adding an optional parameter should not break a wrapper.
        /// </summary>
        private MethodInfo Choose(int argCount)
        {
            if (_methods is null) return null;
            return _methods.FirstOrDefault(m => m.GetParameters().Length == argCount)
                ?? _methods.Where(m => m.GetParameters().Length > argCount)
                           .OrderBy(m => m.GetParameters().Length)
                           .FirstOrDefault();
        }

        /// <summary>Fills parameters the caller did not supply with their defaults.</summary>
        private static object[] Pad(MethodInfo method, object[] args)
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 0) return null;

            var full = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                if (args != null && i < args.Length) { full[i] = args[i]; continue; }
                var p = parameters[i];
                full[i] = p.HasDefaultValue ? p.DefaultValue
                    : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType)
                    : null;
            }
            return full;
        }

        /// <summary>Forgets the binding. Called on scene unload with the singleton cache.</summary>
        internal void Forget()
        {
            _resolved = false;
            _property = null; _field = null; _methods = null;
            BoundTo = null;
        }
    }
}
