/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.EndpointRegistry.Server;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.EndpointRegistry.Federation.Server
{
    /// <summary>
    /// Hosts read-only Group references using verified observations. Pins and complete source
    /// records belong to the existing host's CAS generation, never a second store or epoch.
    /// </summary>
    [SuppressMessage("Usage", "CA2213",
        Justification = "Captured host observer callbacks can outlive detachment; these gates never allocate WaitHandles.")]
    public sealed class EndpointRegistryGroupFederationBinding : IRegistryStateObserver, IAsyncDisposable
    {
        /// <summary>
        /// Constructs a binding after the registry address space is initialized.
        /// Call StartAsync to restore pins; call RefreshAsync explicitly to authenticate sources.
        /// </summary>
        public EndpointRegistryGroupFederationBinding(
            EndpointRegistryNodeManager registry, EndpointRegistryGroupFederationOptions options)
        {
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            m_root = options.Root;
            if (m_root is not (EndpointRegistryRoot.Generic or EndpointRegistryRoot.Media))
            {
                throw new ArgumentException("Select an existing generic or media Endpoint Registry.", nameof(options));
            }
            m_host = (m_root == EndpointRegistryRoot.Generic ? registry.Generic : registry.Media) ??
                throw new InvalidOperationException("The selected Endpoint Registry must be initialized.");
            m_prefix = options.IdentifierPrefix;
            if (string.IsNullOrEmpty(m_prefix) || m_prefix.Split('/').Length != 1 || m_prefix is "." or ".." ||
                options.Sources.Count == 0)
            {
                throw new ArgumentException("A nonempty identifier prefix and explicit sources are required.", nameof(options));
            }
            foreach (GroupFederationSource source in options.Sources)
            {
                string collection = Collection(source.LocalXid);
                if (Collection(source.RemoteXid) != collection ||
                    !source.LocalXid.Split('/')[2].StartsWith(m_prefix, StringComparison.Ordinal) ||
                    !m_sources.TryAdd(source.LocalXid, source))
                {
                    throw new ArgumentException("Selections require unique reserved local Group Xids in the source collection.",
                        nameof(options));
                }
            }
            foreach (GroupFederationSource source in m_sources.Values)
            {
                string collection = Collection(source.LocalXid);
                if (!m_host.Collections.Contains(collection))
                {
                    throw new ArgumentException("Every source collection must be hosted by the selected registry.", nameof(options));
                }
            }
            foreach (GroupFederationSource source in m_sources.Values)
            {
                string collection = Collection(source.LocalXid);
                if (!m_providers.ContainsKey(collection))
                {
                    m_providers.Add(collection, m_host.CreateProvider(collection, m_prefix));
                }
            }
        }

        /// <summary>
        /// Restores and validates pins without authenticating any peer or exposing a locator
        /// or native target. This operation never invokes the observation boundary.
        /// </summary>
        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            await m_operations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (m_started)
                {
                    throw new InvalidOperationException("The binding is already started.");
                }
                ReadPins(m_host.Current);
                m_host.AddObserver(this);
                m_started = true;
                try
                {
                    await PublishAsync(m_host.Current, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    m_started = false;
                    m_host.RemoveObserver(this);
                    throw;
                }
            }
            finally
            {
                m_operations.Release();
            }
        }

        /// <summary>
        /// Gets a verified observation only while its committed local Object is active.
        /// Restored pins, failed refreshes, retirement and disposal return null.
        /// </summary>
        public FederationGroupSnapshot? GetVerifiedSource(string localXid)
        {
            lock (m_gate)
            {
                return m_started && !m_disposed && m_visible &&
                    m_verified.TryGetValue(localXid, out FederationGroupSnapshot? value) ? value : null;
            }
        }

        /// <summary>
        /// Obtains a fresh configured observation and atomically commits its full metadata
        /// and identity pins. A rejected observation leaves the durable generation unchanged
        /// and removes the previous transport exposure.
        /// </summary>
        public async ValueTask RefreshAsync(string localXid, CancellationToken cancellationToken = default)
        {
            await m_operations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureStarted();
                GroupFederationSource source = Selection(localXid);
                await InvalidateCoreAsync(localXid, cancellationToken).ConfigureAwait(false);
                FederationGroupSnapshot observed = await source.Observe(cancellationToken).ConfigureAwait(false);
                ValidateObservation(source, observed);
                Dictionary<string, GroupFederationPins> pins = ReadPins(m_host.Current);
                if (pins.TryGetValue(localXid, out GroupFederationPins? previous))
                {
                    previous.Match(observed, m_host.Mapper);
                }
                RegistryObjectValueDataType record = GroupFederationPins.Encode(observed, m_host.Mapper);
                _ = new GroupFederationPins(record, source, m_host.Mapper);
                RegistryObjectValueDataType definition = GroupFederationPins.LocalDefinition(
                    observed, source.LocalXid, record, m_host.Mapper);
                lock (m_gate)
                {
                    m_pending[localXid] = observed;
                }
                try
                {
                    RegistryMutationResultDataType result = await m_providers[Collection(localXid)].ReplaceAsync(
                        localXid, m_host.Mapper.Project(definition,
                            Collection(localXid) == "endpoints" ? nameof(EndpointDataType) : nameof(MessageGroupDataType)),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (!StatusCode.IsGood(result.StatusCode))
                    {
                        throw new ServiceResultException(result.StatusCode,
                            result.Issues.Count == 0 ? "Group activation failed." : result.Issues[0].Detail ?? "Group activation failed.");
                    }
                    // A byte-identical CAS no-op does not send activation notifications.
                    await PublishAsync(m_host.Current, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    await InvalidateCoreAsync(localXid, CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                finally
                {
                    lock (m_gate)
                    {
                        m_pending.Remove(localXid);
                    }
                }
            }
            finally
            {
                m_operations.Release();
            }
        }

        /// <summary>Revokes transport exposure without removing the durable identity pins.</summary>
        public async ValueTask InvalidateAsync(string localXid, CancellationToken cancellationToken = default)
        {
            await m_operations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureStarted();
                Selection(localXid);
                await InvalidateCoreAsync(localXid, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_operations.Release();
            }
        }

        /// <summary>Deletes a local reference and its pins through the authoritative commit path.</summary>
        public async ValueTask RemoveAsync(string localXid, CancellationToken cancellationToken = default)
        {
            await m_operations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureStarted();
                Selection(localXid);
                await InvalidateCoreAsync(localXid, cancellationToken).ConfigureAwait(false);
                RegistryMutationResultDataType result = await m_providers[Collection(localXid)]
                    .DeleteAsync(localXid, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!StatusCode.IsGood(result.StatusCode))
                {
                    throw new ServiceResultException(result.StatusCode);
                }
            }
            finally
            {
                m_operations.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask ActivatingAsync(RegistryCommittedState state, CancellationToken cancellationToken)
        {
            await m_projection.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (m_gate)
                {
                    m_visible = false;
                    m_activating = true;
                }
                foreach (string xid in m_sources.Keys)
                {
                    if (m_registry.FindCatalogObject(xid, m_root) is { } node)
                    {
                        m_registry.InvalidateGroupFederation(node);
                    }
                }
            }
            finally
            {
                m_projection.Release();
            }
        }

        /// <inheritdoc/>
        public ValueTask ActivatedAsync(RegistryCommittedState state, CancellationToken cancellationToken) =>
            PublishAsync(state, cancellationToken, activated: true);

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await m_operations.WaitAsync().ConfigureAwait(false);
            try
            {
                if (m_disposed)
                {
                    return;
                }
                lock (m_gate)
                {
                    m_verified.Clear();
                    m_pending.Clear();
                    m_started = false;
                    m_disposed = true;
                    m_visible = false;
                }
                m_host.RemoveObserver(this);
                await ActivatingAsync(m_host.Current, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                m_operations.Release();
            }
        }

        private async ValueTask PublishAsync(
            RegistryCommittedState state, CancellationToken cancellationToken, bool activated = false)
        {
            await m_projection.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (m_disposed || !m_started)
                {
                    return;
                }
                lock (m_gate)
                {
                    if (m_activating && !activated)
                    {
                        return;
                    }
                    m_visible = false;
                }
                Dictionary<string, GroupFederationPins> pins = ReadPins(state);
                foreach (string xid in m_sources.Keys)
                {
                    FederationGroupSnapshot? verified;
                    lock (m_gate)
                    {
                        m_pending.TryGetValue(xid, out verified);
                        if (verified is null)
                        {
                            m_verified.TryGetValue(xid, out verified);
                        }
                    }
                    if (!pins.TryGetValue(xid, out GroupFederationPins? pin) ||
                        verified is not null && !RegistryValues.Identical(pin.Value,
                            GroupFederationPins.Encode(verified, m_host.Mapper)))
                    {
                        verified = null;
                    }
                    BaseObjectState? node = pin is null ? null : m_registry.FindCatalogObject(xid, m_root);
                    if (node is null)
                    {
                        verified = null;
                    }
                    lock (m_gate)
                    {
                        if (verified is null)
                        {
                            m_verified.Remove(xid);
                        }
                        else
                        {
                            m_verified[xid] = verified;
                        }
                    }
                    if (pin is not null && node is not null)
                    {
                        await m_registry.SetGroupFederationAsync(node, pin.Origin,
                            verified?.Source.NativeTarget ?? ExpandedNodeId.Null,
                            verified?.Source.Locator ?? string.Empty, pin.Metadata, pin.Epoch,
                            verified is not null, verified?.ApplicationUri ?? string.Empty, cancellationToken).ConfigureAwait(false);
                    }
                }
                lock (m_gate)
                {
                    m_visible = true;
                    m_activating = false;
                }
            }
            catch
            {
                lock (m_gate)
                {
                    m_visible = false;
                    m_verified.Clear();
                }
                foreach (string xid in m_sources.Keys)
                {
                    if (m_registry.FindCatalogObject(xid, m_root) is { } node)
                    {
                        m_registry.InvalidateGroupFederation(node);
                    }
                }
                throw;
            }
            finally
            {
                m_projection.Release();
            }
        }

        private async ValueTask InvalidateCoreAsync(string xid, CancellationToken cancellationToken)
        {
            await m_projection.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (m_gate)
                {
                    m_verified.Remove(xid);
                }
                if (m_registry.FindCatalogObject(xid, m_root) is { } node)
                {
                    m_registry.InvalidateGroupFederation(node);
                }
            }
            finally
            {
                m_projection.Release();
            }
        }

        private Dictionary<string, GroupFederationPins> ReadPins(RegistryCommittedState state)
        {
            var result = new Dictionary<string, GroupFederationPins>(StringComparer.Ordinal);
            RegistryObjectValueDataType document = state.CloneDocument();
            foreach (string collection in m_providers.Keys)
            {
                if (GroupFederationPins.Member(document, collection) is not RegistryObjectValueDataType groups)
                {
                    continue;
                }
                foreach (RegistryMemberDataType group in groups.Members)
                {
                    if (!group.Name!.StartsWith(m_prefix, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string xid = "/" + collection + "/" + group.Name;
                    GroupFederationSource source = Selection(xid);
                    if (group.Value is not RegistryObjectValueDataType value ||
                        GroupFederationPins.Member(value, GroupFederationPins.Attribute) is not RegistryObjectValueDataType record)
                    {
                        throw new ArgumentException("A reserved Group has missing durable federation pins.");
                    }
                    result.Add(xid, new GroupFederationPins(record, source, m_host.Mapper));
                }
            }
            return result;
        }

        private static void ValidateObservation(GroupFederationSource source, FederationGroupSnapshot observation)
        {
            if (observation is null || !source.Trust.Origin.Equals(observation.Origin) ||
                source.Trust.ApplicationUri != observation.ApplicationUri ||
                source.Trust.RegistryRoot != observation.RegistryRoot ||
                observation.Source.Xid != source.RemoteXid || observation.Source.Role != "Group" ||
                !source.Trust.Authorizes(observation.Source.Locator!))
            {
                throw new ArgumentException("The fresh Group observation contradicts the configured source binding.");
            }
        }

        private GroupFederationSource Selection(string xid) =>
            m_sources.TryGetValue(xid, out GroupFederationSource? source) ? source :
                throw new ArgumentException("The local Group has no configured source selection.", nameof(xid));

        private static string Collection(string xid)
        {
            string[] parts = xid.Split('/');
            if (parts.Length != 3 || parts[0].Length != 0 ||
                parts[1] is not ("endpoints" or "messagegroups") ||
                parts[2].Length == 0 || parts[2] is "." or "..")
            {
                throw new ArgumentException("A concrete collection-qualified Group Xid is required.", nameof(xid));
            }
            return parts[1];
        }

        private void EnsureStarted()
        {
            ThrowIfDisposed();
            if (!m_started)
            {
                throw new InvalidOperationException("Start the Group federation binding first.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(EndpointRegistryGroupFederationBinding));
            }
        }

        private readonly EndpointRegistryNodeManager m_registry;
        private readonly RegistryNativeHost m_host;
        private readonly EndpointRegistryRoot m_root;
        private readonly string m_prefix;
        private readonly Dictionary<string, GroupFederationSource> m_sources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RegistryNativeProvider> m_providers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FederationGroupSnapshot> m_verified = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FederationGroupSnapshot> m_pending = new(StringComparer.Ordinal);
        private readonly Lock m_gate = new();
        // No WaitHandle is allocated. Retained gates let already-captured observer callbacks
        // drain safely after detachment and let later public calls report ObjectDisposedException.
        private readonly SemaphoreSlim m_operations = new(1, 1);
        private readonly SemaphoreSlim m_projection = new(1, 1);
        private bool m_started;
        private bool m_visible;
        private bool m_activating;
        private bool m_disposed;
    }
}
