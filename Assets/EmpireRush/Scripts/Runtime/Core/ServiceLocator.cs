using System;
using System.Collections.Generic;

namespace EmpireRush
{
    /// <summary>
    /// Minimal typed registry used to wire systems without dragging singletons through every
    /// constructor. Registration happens once, in <see cref="GameBootstrap"/>, in dependency order.
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>(16);

        public static void Register<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            Services[typeof(T)] = service;
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            if (Services.TryGetValue(typeof(T), out object found) && found is T typed)
            {
                service = typed;
                return true;
            }

            service = null;
            return false;
        }

        public static T Get<T>() where T : class
        {
            if (TryGet<T>(out T service)) return service;
            throw new InvalidOperationException($"ServiceLocator has no registration for {typeof(T).Name}.");
        }

        public static bool IsRegistered<T>() where T : class => Services.ContainsKey(typeof(T));

        public static void Unregister<T>() where T : class => Services.Remove(typeof(T));

        /// <summary>Drops every registration. Called by <see cref="GameBootstrap"/> on teardown.</summary>
        public static void Clear() => Services.Clear();
    }
}
