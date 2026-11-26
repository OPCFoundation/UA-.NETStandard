// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Provides the server-wide cache of decoded namespace defaults.
    /// </summary>
    internal interface INamespaceDefaultPermissionCacheProvider
    {
        /// <summary>
        /// The cache of the server the provider belongs to.
        /// </summary>
        NamespaceDefaultPermissionCache DefaultPermissionCache { get; }
    }

    /// <summary>
    /// Server-wide cache of the decoded namespace defaults of every namespace whose defaults are
    /// static: stored values readable without a callback, so the decoded value does not depend on
    /// the caller. Long-lived operation contexts (monitored items, subscribers) read through it
    /// without taking the lock of the metadata properties, and service requests no longer decode
    /// a namespace's defaults once per request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An entry is the result of one decode, stamped with the
    /// <see cref="NamespaceDefaultPermissionReader.Generation"/> it was read in. It is used only
    /// while all of the following hold, each checked on every hit without a lock:
    /// </para>
    /// <list type="bullet">
    /// <item>The generation is unchanged. The <see cref="NamespaceMetadataRegistry"/> moves it
    /// whenever a default value is reported changed (<c>ClearChangeMasks</c>), a default
    /// property is added, replaced or removed, and a metadata node is linked, relinked or
    /// removed, which includes a node manager's namespace going away through the lifecycle.</item>
    /// <item>No default property has a pending, not yet reported value change. A value assigned
    /// in process without <c>ClearChangeMasks</c> therefore applies to the very next check, as it
    /// did before the cache, and such a namespace is read live until the change is reported.</item>
    /// <item>The metadata node still exposes the same default properties, and each of them is
    /// still static (no read callback, read access in both access levels).</item>
    /// </list>
    /// <para>
    /// Entries are indexed by namespace index in an array that only grows, so a hit is one array
    /// load and a few field reads.
    /// </para>
    /// </remarks>
    internal sealed class NamespaceDefaultPermissionCache
    {
        /// <summary>
        /// Returns the cached defaults of <paramref name="namespaceIndex"/> when they are current.
        /// </summary>
        public bool TryGet(ushort namespaceIndex, out NamespaceDefaultPermissions permissions)
        {
            Entry?[] entries = Volatile.Read(ref m_entries);
            if (namespaceIndex < entries.Length)
            {
                Entry? entry = Volatile.Read(ref entries[namespaceIndex]);
                if (entry != null &&
                    entry.Value.Generation == NamespaceDefaultPermissionReader.Generation &&
                    entry.IsCurrent())
                {
                    permissions = Copy(entry.Value);
                    return true;
                }
            }

            permissions = default;
            return false;
        }

        /// <summary>
        /// Remembers <paramref name="permissions"/>, decoded from <paramref name="metadata"/>, when
        /// they are good, of the current generation, and the defaults are static.
        /// </summary>
        /// <param name="metadata">The metadata node the defaults were decoded from.</param>
        /// <param name="accessRestrictions">The DefaultAccessRestrictions property that was read.</param>
        /// <param name="rolePermissions">The DefaultRolePermissions property that was read.</param>
        /// <param name="userRolePermissions">The DefaultUserRolePermissions property that was read.</param>
        /// <param name="permissions">The decoded defaults.</param>
        /// <param name="staticBeforeDecode">Whether the three properties were static before the
        /// decode; a callback that removed itself while it ran produced a caller-specific result.</param>
        public void TryAdd(
            NamespaceMetadataNode? metadata,
            BaseVariableState? accessRestrictions,
            BaseVariableState? rolePermissions,
            BaseVariableState? userRolePermissions,
            NamespaceDefaultPermissions permissions,
            bool staticBeforeDecode)
        {
            // a namespace without metadata is not cached: the registry answers that from its own
            // miss cache, which also observes unreported changes of Server/Namespaces.
            if (metadata == null ||
                !staticBeforeDecode ||
                metadata.IsIncomplete ||
                !StatusCode.IsGood(permissions.Status) ||
                permissions.Generation != NamespaceDefaultPermissionReader.Generation ||
                !NamespaceDefaultPermissionReader.IsStatic(accessRestrictions) ||
                !NamespaceDefaultPermissionReader.IsStatic(rolePermissions) ||
                !NamespaceDefaultPermissionReader.IsStatic(userRolePermissions))
            {
                return;
            }

            var entry = new Entry(metadata, accessRestrictions, rolePermissions, userRolePermissions, Copy(permissions));
            if (!entry.IsCurrent())
            {
                return;
            }

            ushort namespaceIndex = permissions.NamespaceIndex;
            Entry?[] entries = Volatile.Read(ref m_entries);
            if (namespaceIndex >= entries.Length)
            {
                lock (m_lock)
                {
                    entries = m_entries;
                    if (namespaceIndex >= entries.Length)
                    {
                        int length = Math.Max(16, entries.Length);
                        while (length <= namespaceIndex)
                        {
                            length *= 2;
                        }

                        var grown = new Entry?[Math.Min(length, ushort.MaxValue + 1)];
                        for (int ii = 0; ii < entries.Length; ii++)
                        {
                            grown[ii] = Volatile.Read(ref entries[ii]);
                        }

                        Volatile.Write(ref m_entries, grown);
                        entries = grown;
                    }
                }
            }

            // a store into an array that a concurrent grow just replaced is lost; that only
            // costs a second decode.
            Volatile.Write(ref entries[namespaceIndex], entry);
        }

        /// <summary>
        /// Drops every entry.
        /// </summary>
        public void Clear()
        {
            lock (m_lock)
            {
                Volatile.Write(ref m_entries, []);
            }
        }

        /// <summary>
        /// Copies the role permission entries, which are mutable: callers receive them through
        /// public <see cref="NodeMetadata"/>, and a caller that changes them must not change the
        /// defaults of later requests. A service request shares one copy across its nodes.
        /// </summary>
        private static NamespaceDefaultPermissions Copy(NamespaceDefaultPermissions permissions)
        {
            return new NamespaceDefaultPermissions(
                permissions.NamespaceIndex,
                permissions.DefaultAccessRestrictions,
                Copy(permissions.DefaultRolePermissions),
                Copy(permissions.DefaultUserRolePermissions),
                permissions.Generation,
                permissions.Status,
                permissions.HasDefaultAccessRestrictions,
                permissions.HasDefaultRolePermissions,
                permissions.HasDefaultUserRolePermissions);
        }

        private static ArrayOf<RolePermissionType> Copy(ArrayOf<RolePermissionType> roles)
        {
            if (roles.IsNull || roles.Count == 0)
            {
                return roles;
            }

            var copy = new RolePermissionType[roles.Count];
            for (int ii = 0; ii < copy.Length; ii++)
            {
                copy[ii] = (RolePermissionType)roles[ii]?.Clone()!;
            }

            return copy;
        }

        /// <summary>
        /// A decoded result and the properties it was decoded from.
        /// </summary>
        private sealed class Entry
        {
            public Entry(
                NamespaceMetadataNode metadata,
                BaseVariableState? accessRestrictions,
                BaseVariableState? rolePermissions,
                BaseVariableState? userRolePermissions,
                NamespaceDefaultPermissions value)
            {
                m_metadata = metadata;
                m_accessRestrictions = accessRestrictions;
                m_rolePermissions = rolePermissions;
                m_userRolePermissions = userRolePermissions;
                Value = value;
            }

            public NamespaceDefaultPermissions Value { get; }

            /// <summary>
            /// Lock-free check that the properties the value was decoded from are still the
            /// default properties, are static and have no unreported value change.
            /// </summary>
            public bool IsCurrent()
            {
                return ReferenceEquals(m_metadata.DefaultAccessRestrictions, m_accessRestrictions) &&
                    ReferenceEquals(m_metadata.DefaultRolePermissions, m_rolePermissions) &&
                    ReferenceEquals(m_metadata.DefaultUserRolePermissions, m_userRolePermissions) &&
                    IsUnchanged(m_accessRestrictions) &&
                    IsUnchanged(m_rolePermissions) &&
                    IsUnchanged(m_userRolePermissions);
            }

            private static bool IsUnchanged(BaseVariableState? property)
            {
                return property == null ||
                    ((property.ChangeMasks & NodeStateChangeMasks.Value) == 0 &&
                        NamespaceDefaultPermissionReader.IsStatic(property));
            }

            private readonly NamespaceMetadataNode m_metadata;
            private readonly BaseVariableState? m_accessRestrictions;
            private readonly BaseVariableState? m_rolePermissions;
            private readonly BaseVariableState? m_userRolePermissions;
        }

        private readonly Lock m_lock = new();
        private Entry?[] m_entries = [];
    }
}
