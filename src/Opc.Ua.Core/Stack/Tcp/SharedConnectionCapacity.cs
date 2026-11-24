// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Lets the connection admission scopes that share one resource isolation
    /// provider reclaim idle connections from each other.
    /// </summary>
    /// <remarks>
    /// The provider's connection capacity is shared by every listener of a
    /// server, while each listener can only reclaim its own idle connections.
    /// Without this, idle connections held on one listener (for example HTTP
    /// keep-alive connections on opc.https) refuse every new connection on all
    /// the other listeners (opc.tcp, opc.wss) although they are reclaimable.
    /// </remarks>
    internal static class SharedConnectionCapacity
    {
        /// <summary>
        /// Registers the reclaim callback of an admission scope, replacing an
        /// earlier registration of the same scope.
        /// </summary>
        public static void Register(
            IServerResourceIsolationProvider provider,
            UaScConnectionAdmission scope,
            Func<bool> reclaim)
        {
            s_registries.GetValue(provider, static _ => new Registry()).Register(scope, reclaim);
        }

        /// <summary>
        /// Removes the registration of an admission scope.
        /// </summary>
        public static void Unregister(IServerResourceIsolationProvider provider, UaScConnectionAdmission scope)
        {
            if (s_registries.TryGetValue(provider, out Registry? registry))
            {
                registry.Unregister(scope);
            }
        }

        /// <summary>
        /// Whether another scope than <paramref name="scope"/> could release capacity.
        /// </summary>
        public static bool HasOtherScopes(IServerResourceIsolationProvider provider, UaScConnectionAdmission scope)
        {
            return s_registries.TryGetValue(provider, out Registry? registry) && registry.HasOtherScopes(scope);
        }

        /// <summary>
        /// Asks the other scopes, one after the other, to close one reclaimable
        /// connection. Returns true as soon as one of them did.
        /// </summary>
        /// <remarks>
        /// The callbacks run without any registry or admission lock held, as
        /// they close connections.
        /// </remarks>
        public static bool TryReclaimFromOtherScopes(
            IServerResourceIsolationProvider provider,
            UaScConnectionAdmission scope)
        {
            if (!s_registries.TryGetValue(provider, out Registry? registry))
            {
                return false;
            }
            foreach (Entry entry in registry.Snapshot)
            {
                if (!ReferenceEquals(entry.Scope, scope) && entry.Reclaim())
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A registered scope and its reclaim callback.
        /// </summary>
        private sealed class Entry(UaScConnectionAdmission scope, Func<bool> reclaim)
        {
            public UaScConnectionAdmission Scope { get; } = scope;

            public Func<bool> Reclaim { get; } = reclaim;
        }

        /// <summary>
        /// The scopes registered for one provider, published as an immutable snapshot.
        /// </summary>
        private sealed class Registry
        {
            public Entry[] Snapshot => Volatile.Read(ref m_entries);

            public void Register(UaScConnectionAdmission scope, Func<bool> reclaim)
            {
                lock (m_lock)
                {
                    Entry[] entries = m_entries;
                    Entry[] updated = new Entry[entries.Length + 1];
                    int count = 0;
                    foreach (Entry entry in entries)
                    {
                        if (!ReferenceEquals(entry.Scope, scope))
                        {
                            updated[count++] = entry;
                        }
                    }
                    updated[count++] = new Entry(scope, reclaim);
                    Array.Resize(ref updated, count);
                    Volatile.Write(ref m_entries, updated);
                }
            }

            public void Unregister(UaScConnectionAdmission scope)
            {
                lock (m_lock)
                {
                    Entry[] entries = m_entries;
                    int index = Array.FindIndex(entries, entry => ReferenceEquals(entry.Scope, scope));
                    if (index < 0)
                    {
                        return;
                    }
                    Entry[] updated = new Entry[entries.Length - 1];
                    Array.Copy(entries, 0, updated, 0, index);
                    Array.Copy(entries, index + 1, updated, index, entries.Length - index - 1);
                    Volatile.Write(ref m_entries, updated);
                }
            }

            public bool HasOtherScopes(UaScConnectionAdmission scope)
            {
                foreach (Entry entry in Snapshot)
                {
                    if (!ReferenceEquals(entry.Scope, scope))
                    {
                        return true;
                    }
                }
                return false;
            }

            private readonly Lock m_lock = new();
            private Entry[] m_entries = [];
        }

        /// <summary>
        /// The registrations per provider; a provider that is no longer referenced
        /// takes its registrations with it.
        /// </summary>
        private static readonly ConditionalWeakTable<IServerResourceIsolationProvider, Registry> s_registries = new();
    }
}
