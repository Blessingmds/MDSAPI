using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using MDSAPI.Logging;

namespace MDSAPI.Internal.Interop
{
    /// <summary>
    /// A field or property on a game type, read and written through a compiled delegate.
    /// <para>
    /// <see cref="MemberSpec"/> reaches members of singletons; this reaches members of any
    /// game object a mod holds — a slot, an item, a rack — and is built for hot paths. The
    /// member is found once by candidate name (property or field, on the type or any base),
    /// then compiled to a delegate, so a read costs about what the direct C# access costs.
    /// If compiling is not possible on this runtime, it falls back to reflection and says
    /// so once.
    /// </para>
    /// </summary>
    internal sealed class Access<T>
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Instance | BindingFlags.Static |
                                           BindingFlags.DeclaredOnly;

        private readonly string _typeName;
        private readonly string[] _candidates;

        private bool _resolved;
        private Type _declaring;
        private PropertyInfo _property;
        private FieldInfo _field;
        private bool _static;
        private Func<object, T> _get;
        private Action<object, T> _set;
        private bool _setWarned;

        internal string BoundTo { get; private set; }

        internal bool IsAvailable { get { Resolve(); return BoundTo != null; } }

        internal Access(string typeName, params string[] candidates)
        {
            _typeName = typeName;
            _candidates = candidates;
        }

        /// <summary>Reads the member of <paramref name="instance"/> (ignored if static).</summary>
        internal T Get(object instance, T fallback = default)
        {
            Resolve();
            if (_get is null || (!_static && instance is null)) return fallback;
            try { return _get(instance); }
            catch { return fallback; }
        }

        /// <summary>Writes the member. False if it is missing or read-only.</summary>
        internal bool Set(object instance, T value)
        {
            Resolve();
            if (BoundTo is null || (!_static && instance is null)) return false;
            if (_set is null)
            {
                if (!_setWarned)
                {
                    _setWarned = true;
                    Log.Warn(_typeName + "." + BoundTo + " cannot be written on this game version.");
                }
                return false;
            }
            try { _set(instance, value); return true; }
            catch (Exception ex)
            {
                Log.Error("Writing " + _typeName + "." + BoundTo + " threw.", ex);
                return false;
            }
        }

        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            var type = GameTypes.Find(_typeName);
            if (type is null)
            {
                Log.Warn("Game type " + _typeName + " was not found; " + _typeName + "." +
                         _candidates[0] + " is unavailable this session.");
                return;
            }

            foreach (var name in _candidates)
            {
                for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                {
                    var p = t.GetProperty(name, Flags);
                    if (p != null && p.GetIndexParameters().Length == 0) { _property = p; break; }
                    var f = t.GetField(name, Flags);
                    if (f != null) { _field = f; break; }
                }
                if (_property is null && _field is null) continue;

                _declaring = type;
                _static = _property != null
                    ? (_property.GetGetMethod(true) ?? _property.GetSetMethod(true)).IsStatic
                    : _field.IsStatic;
                BoundTo = name;
                if (name != _candidates[0])
                    Log.Warn(_typeName + "." + _candidates[0] + " was not found; using '" + name +
                             "' instead. The game has likely been updated — this still works, " +
                             "but it is worth reporting.");
                Build();
                return;
            }

            Log.Warn("Could not find " + _typeName + "." + _candidates[0] + " (tried " +
                     string.Join(", ", _candidates) + "). Whatever uses it is unavailable this session.");
        }

        private void Build()
        {
            try { BuildCompiled(); }
            catch (Exception ex)
            {
                Accessors.NoteFallback(_typeName + "." + BoundTo, ex);
                BuildReflective();
            }
        }

        private Expression Instance(ParameterExpression o)
        {
            if (_static) return null;
            var declaring = _property?.DeclaringType ?? _field.DeclaringType;
#if MDS_IL2CPP
            // Under IL2CPP the object can arrive wrapped as a base type; ask the native side.
            var caster = Cast.CasterFor(declaring);
            return Expression.Convert(Expression.Invoke(Expression.Constant(caster), o), declaring);
#else
            return Expression.Convert(o, declaring);
#endif
        }

        private void BuildCompiled()
        {
            var o = Expression.Parameter(typeof(object), "o");
            var member = _property != null
                ? (Expression)Expression.Property(Instance(o), _property)
                : Expression.Field(Instance(o), _field);

            var canRead = _property == null || _property.GetGetMethod(true) != null;
            if (canRead)
            {
                Expression body = member.Type == typeof(T) ? member : Expression.Convert(member, typeof(T));
                _get = Expression.Lambda<Func<object, T>>(body, o).Compile();
            }

            var canWrite = _property != null
                ? _property.GetSetMethod(true) != null
                : !_field.IsInitOnly && !_field.IsLiteral;
            if (canWrite)
            {
                var v = Expression.Parameter(typeof(T), "v");
                Expression value = member.Type == typeof(T) ? (Expression)v : Expression.Convert(v, member.Type);
                _set = Expression.Lambda<Action<object, T>>(Expression.Assign(member, value), o, v).Compile();
            }
        }

        private void BuildReflective()
        {
            var declaring = _property?.DeclaringType ?? _field.DeclaringType;
            Func<object, object> target = _static ? (Func<object, object>)(_ => null) : o => Cast.To(o, declaring);

            if (_property != null)
            {
                var getter = _property.GetGetMethod(true);
                var setter = _property.GetSetMethod(true);
                if (getter != null) _get = o => Accessors.Convert<T>(getter.Invoke(target(o), null));
                if (setter != null) _set = (o, v) => setter.Invoke(target(o), new object[] { v });
            }
            else
            {
                var f = _field;
                _get = o => Accessors.Convert<T>(f.GetValue(target(o)));
                if (!f.IsInitOnly && !f.IsLiteral) _set = (o, v) => f.SetValue(target(o), v);
            }
        }
    }

    /// <summary>Shared bits for <see cref="Access{T}"/>.</summary>
    internal static class Accessors
    {
        private static bool _noted;

        /// <summary>
        /// True once any accessor fell back to reflection. Reported once: it costs speed,
        /// not correctness, and the self-test shows which mode is live.
        /// </summary>
        internal static bool UsingReflection { get; private set; }

        internal static void NoteFallback(string what, Exception ex)
        {
            UsingReflection = true;
            if (_noted) return;
            _noted = true;
            Log.Warn("Compiled member access is unavailable on this runtime (" + what + ": " +
                     ex.GetType().Name + "); using reflection. Everything works, a little slower.");
        }

        internal static T Convert<T>(object value)
        {
            if (value is T t) return t;
            if (value is null) return default;
            try { return (T)System.Convert.ChangeType(value, typeof(T)); }
            catch { return default; }
        }
    }

    /// <summary>
    /// A method on a game type, called by reflection. For calls a mod makes in response to
    /// something (a click, a drop), not per frame.
    /// </summary>
    internal sealed class GameMethod
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Instance | BindingFlags.Static;

        private readonly string _typeName;
        private readonly string[] _candidates;
        private readonly string _firstParameterType;
        private bool _resolved;
        private MethodInfo _method;
        private ParameterInfo[] _parameters;

        internal string BoundTo { get; private set; }

        internal bool IsAvailable { get { Resolve(); return _method != null; } }

        internal MethodInfo Method { get { Resolve(); return _method; } }

        /// <param name="typeName">Game type relative to the root.</param>
        /// <param name="firstParameterType">
        /// When the name is overloaded, the short type name of the first parameter of the
        /// wanted overload ("ExitDelegate"); null to take the one with the fewest parameters.
        /// </param>
        /// <param name="candidates">Method names, current first.</param>
        internal GameMethod(string typeName, string firstParameterType, params string[] candidates)
        {
            _typeName = typeName;
            _firstParameterType = firstParameterType;
            _candidates = candidates;
        }

        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            var type = GameTypes.Find(_typeName);
            if (type is null)
            {
                Log.Warn("Game type " + _typeName + " was not found; " + _typeName + "." +
                         _candidates[0] + " is unavailable this session.");
                return;
            }

            foreach (var name in _candidates)
            {
                var overloads = type.GetMethods(Flags).Where(m => m.Name == name && !m.IsGenericMethodDefinition);
                if (_firstParameterType != null)
                    overloads = overloads.Where(m =>
                    {
                        var ps = m.GetParameters();
                        return ps.Length > 0 && ps[0].ParameterType.Name == _firstParameterType;
                    });
                var chosen = overloads.OrderBy(m => m.GetParameters().Length).FirstOrDefault();
                if (chosen is null) continue;

                _method = chosen;
                _parameters = chosen.GetParameters();
                BoundTo = name;
                if (name != _candidates[0])
                    Log.Warn(_typeName + "." + _candidates[0] + " was not found; using '" + name +
                             "' instead. The game has likely been updated — this still works, " +
                             "but it is worth reporting.");
                return;
            }

            Log.Warn("Could not find " + _typeName + "." + _candidates[0] + " (tried " +
                     string.Join(", ", _candidates) + "). Whatever uses it is unavailable this session.");
        }

        /// <summary>
        /// Calls the method. Missing trailing arguments take their defaults; game objects are
        /// cast to the parameter types. Throws only what the game method throws.
        /// </summary>
        internal object Invoke(object instance, params object[] args)
        {
            Resolve();
            if (_method is null) throw new MissingMethodException(_typeName, _candidates[0]);

            var target = _method.IsStatic ? null : Cast.To(instance, _method.DeclaringType);
            if (!_method.IsStatic && target is null) throw new NullReferenceException(_typeName + " instance");

            object[] full = null;
            if (_parameters.Length > 0)
            {
                full = new object[_parameters.Length];
                for (var i = 0; i < _parameters.Length; i++)
                {
                    var p = _parameters[i];
                    if (args != null && i < args.Length)
                    {
                        var a = args[i];
                        full[i] = a is null || p.ParameterType.IsInstanceOfType(a) ? a : Cast.To(a, p.ParameterType) ?? a;
                    }
                    else
                        full[i] = p.HasDefaultValue ? p.DefaultValue
                            : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType)
                            : null;
                }
            }
            return _method.Invoke(target, full);
        }

        /// <summary>Calls the method; false (having logged once) if it could not.</summary>
        internal bool TryInvoke(object instance, params object[] args)
        {
            try { Invoke(instance, args); return true; }
            catch (Exception ex)
            {
                Failures.Once(_typeName + "." + (BoundTo ?? _candidates[0]), ex);
                return false;
            }
        }

        /// <summary>Calls the method and converts the result; <paramref name="fallback"/> on failure.</summary>
        internal TResult Call<TResult>(object instance, TResult fallback, params object[] args)
        {
            try { return Accessors.Convert<TResult>(Invoke(instance, args)); }
            catch (Exception ex)
            {
                Failures.Once(_typeName + "." + (BoundTo ?? _candidates[0]), ex);
                return fallback;
            }
        }
    }

    /// <summary>Logs a failure the first time it happens, so per-frame callers cannot spam.</summary>
    internal static class Failures
    {
        private static readonly System.Collections.Generic.HashSet<string> Seen = new();

        internal static void Once(string what, Exception ex)
        {
            if (!Seen.Add(what)) return;
            var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            Log.Error("Calling " + what + " threw (logged once).", inner);
        }

        internal static void Once(string message)
        {
            if (Seen.Add(message)) Log.Error(message);
        }
    }
}
