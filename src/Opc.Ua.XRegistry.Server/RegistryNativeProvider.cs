/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Observes a durable generation before and after its address-space activation.
    /// Implementations must not reenter mutations on the same host.
    /// </summary>
    public interface IRegistryStateObserver
    {
        /// <summary>Invalidates associations before the previous projection is retired.</summary>
        ValueTask ActivatingAsync(RegistryCommittedState state, CancellationToken cancellationToken);

        /// <summary>Publishes associations only after the new projection is active.</summary>
        ValueTask ActivatedAsync(RegistryCommittedState state, CancellationToken cancellationToken);
    }

    /// <summary>
    /// A server-owned capability for one reserved collection identifier prefix.
    /// Uses the host's ordinary validation, epochs, CAS store and activation path.
    /// </summary>
    public sealed class RegistryNativeProvider
    {
        internal RegistryNativeProvider(RegistryNativeHost host, string collection, string prefix)
        {
            m_host = host;
            Collection = collection;
            m_prefix = "/" + collection + "/" + prefix;
        }

        /// <summary>Gets the collection reserved by the provider.</summary>
        public string Collection { get; }

        /// <summary>
        /// Replaces a complete provider-owned Group. Server-owned fields must be absent;
        /// the shared mutation engine preserves them and advances only changed epochs.
        /// </summary>
        public ValueTask<RegistryMutationResultDataType> ReplaceAsync(
            string xid,
            RegistryRecordDataType definition,
            uint expectedEpoch = 0,
            CancellationToken cancellationToken = default)
        {
            return m_host.CommitProviderAsync(this, xid, definition, expectedEpoch, cancellationToken);
        }

        /// <summary>Deletes a provider-owned Group through the same committed-state path.</summary>
        public ValueTask<RegistryMutationResultDataType> DeleteAsync(
            string xid,
            uint expectedEpoch = 0,
            CancellationToken cancellationToken = default)
        {
            return m_host.CommitProviderAsync(this, xid, null, expectedEpoch, cancellationToken);
        }

        internal bool Owns(string xid)
        {
            return xid.StartsWith(m_prefix, StringComparison.Ordinal) &&
                xid.Substring(1).Split('/').Length == 2;
        }

        internal bool Overlaps(RegistryNativeProvider other)
        {
            return m_prefix.StartsWith(other.m_prefix, StringComparison.Ordinal) ||
                other.m_prefix.StartsWith(m_prefix, StringComparison.Ordinal);
        }

        private readonly RegistryNativeHost m_host;
        private readonly string m_prefix;
    }

    public sealed partial class RegistryNativeHost
    {
        /// <summary>
        /// Reserves a nonempty Group identifier prefix for a server provider. Reservation lasts
        /// for the host lifetime and protects direct, ancestor and compatibility mutations.
        /// It is a composition-time capability, not a native Client operation.
        /// </summary>
        public RegistryNativeProvider CreateProvider(string collection, string identifierPrefix)
        {
            if (string.IsNullOrEmpty(identifierPrefix) || identifierPrefix.Split('/').Length != 1 ||
                identifierPrefix is "." or "..")
            {
                throw new ArgumentException("A nonempty identifier prefix is required.", nameof(identifierPrefix));
            }
            bool found = false;
            foreach (string name in m_collections)
            {
                found |= name == collection;
            }
            if (!found)
            {
                throw new ArgumentException("The provider collection is not hosted.", nameof(collection));
            }
            var provider = new RegistryNativeProvider(this, collection, identifierPrefix);
            lock (m_providerGate)
            {
                foreach (RegistryNativeProvider existing in m_providers)
                {
                    if (provider.Overlaps(existing))
                    {
                        throw new InvalidOperationException("Provider scopes cannot overlap.");
                    }
                }
                m_providers.Add(provider);
            }
            return provider;
        }

        /// <summary>Registers a projection observer. Duplicate registration is rejected.</summary>
        public void AddObserver(IRegistryStateObserver observer)
        {
            if (observer is null)
            {
                throw new ArgumentNullException(nameof(observer));
            }
            lock (m_providerGate)
            {
                if (m_stateObservers.Contains(observer))
                {
                    throw new InvalidOperationException("The observer is already registered.");
                }
                m_stateObservers.Add(observer);
            }
        }

        /// <summary>Detaches a projection observer.</summary>
        public void RemoveObserver(IRegistryStateObserver observer)
        {
            lock (m_providerGate)
            {
                m_stateObservers.Remove(observer);
            }
        }

        internal ValueTask<RegistryMutationResultDataType> CommitProviderAsync(
            RegistryNativeProvider provider,
            string xid,
            RegistryRecordDataType? definition,
            uint expectedEpoch,
            CancellationToken cancellationToken)
        {
            return CommitAsync(xid, state =>
            {
                if (!provider.Owns(xid))
                {
                    throw new ServiceResultException(StatusCodes.BadNotWritable,
                        "A provider can only commit Groups in its reserved prefix.");
                }
                string expected = RecordTypeOf(xid, entitiesOnly: true);
                if (definition is null)
                {
                    return RegistryMetadataMutation.Delete(state.Root, xid, expectedEpoch, m_collections,
                        ProtectedPaths(provider: provider), ValidateDocument);
                }
                if (!m_mapper.Catalog.TryGetType(definition.TypeId, out RegistryNativeTypeDescriptor? type) ||
                    !m_mapper.Catalog.IsSubtype(type.Name, expected) ||
                    m_mapper.Restore(definition) is not RegistryObjectValueDataType value)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                        "The provider definition does not match the Group role.");
                }
                return RegistryMetadataMutation.ReplaceOwned(state.Root, xid, value, expectedEpoch, m_collections,
                    ProtectedPaths(provider: provider), ValidateDocument);
            }, cancellationToken);
        }

        private void ValidateOrdinaryDocument(RegistryObjectValueDataType document)
        {
            lock (m_providerGate)
            {
                foreach (RegistryNativeProvider provider in m_providers)
                {
                    foreach (RegistryMemberDataType group in ProviderGroups(document, provider.Collection))
                    {
                        string path = "/" + provider.Collection + "/" + group.Name;
                        if (provider.Owns(path) &&
                            (!TryProviderGroup(Current.Root, provider.Collection, group.Name!, out RegistryValueDataType? old) ||
                            !RegistryValues.Identical(old!, group.Value)))
                        {
                            throw new ServiceResultException(StatusCodes.BadNotWritable,
                                "A provider-owned Group cannot be introduced or changed by an ancestor mutation.");
                        }
                    }
                }
            }
            ValidateDocument(document);
        }

        private static ArrayOf<RegistryMemberDataType> ProviderGroups(
            RegistryObjectValueDataType document, string collection)
        {
            foreach (RegistryMemberDataType member in document.Members)
            {
                if (member.Name == collection && member.Value is RegistryObjectValueDataType groups)
                {
                    return groups.Members;
                }
            }
            return [];
        }

        private static bool TryProviderGroup(
            RegistryObjectValueDataType document, string collection, string name, out RegistryValueDataType? value)
        {
            foreach (RegistryMemberDataType group in ProviderGroups(document, collection))
            {
                if (group.Name == name)
                {
                    value = group.Value;
                    return true;
                }
            }
            value = null;
            return false;
        }

        private async ValueTask NotifyActivatingAsync(RegistryCommittedState state)
        {
            foreach (IRegistryStateObserver observer in StateObservers())
            {
                await observer.ActivatingAsync(state, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async ValueTask NotifyActivatedAsync(RegistryCommittedState state)
        {
            foreach (IRegistryStateObserver observer in StateObservers())
            {
                await observer.ActivatedAsync(state, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private IRegistryStateObserver[] StateObservers()
        {
            lock (m_providerGate)
            {
                return [.. m_stateObservers];
            }
        }

        private readonly Lock m_providerGate = new();
        private readonly List<RegistryNativeProvider> m_providers = [];
        private readonly List<IRegistryStateObserver> m_stateObservers = [];
    }
}
