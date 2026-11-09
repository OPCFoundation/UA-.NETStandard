/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Identity;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Owns the private metadata captured under the lifecycle writer and used by candidate preparation callbacks.
    /// </summary>
    internal sealed class ChangePreparer
    {
        /// <summary>
        /// Captures the serving metadata before invoking any candidate factory.
        /// </summary>
        internal ChangePreparer(IServerInternal server)
        {
            Server = server;
            NamespaceUris = new NamespaceTable(server.NamespaceUris.GetSnapshot(out long namespaceRevision).ToArray()!);
            NamespaceRevision = namespaceRevision;
            ServerUris = new StringTable(server.ServerUris.GetSnapshot(out long serverRevision).ToArray()!);
            ServerRevision = serverRevision;
            TypeTree = server.TypeTree.CaptureSnapshot(
                out TypeTable originalTypes, out long typeRevision, NamespaceUris);
            OriginalTypes = originalTypes;
            TypeRevision = typeRevision;
            if (server.Factory is not EncodeableFactory factory)
            {
                throw new NotSupportedException("The server factory does not support staged registrations.");
            }
            Factory = factory.CaptureSnapshot(out EncodeableFactory originalFactory, out long factoryRevision);
            OriginalFactory = originalFactory;
            FactoryRevision = factoryRevision;
        }

        /// <summary>
        /// Gets the serving server.
        /// </summary>
        internal IServerInternal Server { get; }

        /// <summary>
        /// Gets the privately staged namespaces.
        /// </summary>
        internal NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// Gets the captured namespace revision.
        /// </summary>
        internal long NamespaceRevision { get; }

        /// <summary>
        /// Gets the privately staged server URIs.
        /// </summary>
        internal StringTable ServerUris { get; }

        /// <summary>
        /// Gets the captured server URI revision.
        /// </summary>
        internal long ServerRevision { get; }

        /// <summary>
        /// Gets the private type overlay.
        /// </summary>
        internal TypeTable TypeTree { get; }

        /// <summary>
        /// Gets the captured canonical table.
        /// </summary>
        internal TypeTable OriginalTypes { get; }

        /// <summary>
        /// Gets the captured type revision.
        /// </summary>
        internal long TypeRevision { get; }

        /// <summary>
        /// Gets the private encodeable registrations.
        /// </summary>
        internal EncodeableFactory Factory { get; }

        /// <summary>
        /// Gets a builder that only mutates the private registrations.
        /// </summary>
        internal IEncodeableFactoryBuilder Builder => Factory.Builder;

        /// <summary>
        /// Gets the canonical registration table.
        /// </summary>
        internal EncodeableFactory OriginalFactory { get; }

        /// <summary>
        /// Gets the captured registration revision.
        /// </summary>
        internal long FactoryRevision { get; }

        /// <summary>
        /// Creates independently rebindable handles for a candidate, including contexts copied by its constructor.
        /// </summary>
        internal StagingServer CreateServer()
        {
            return new StagingServer(this);
        }

        /// <summary>
        /// Invokes a candidate factory with its own stable metadata handles.
        /// </summary>
        internal ValueTask<IAsyncNodeManager> CreateNodeManagerAsync(
            IAsyncNodeManagerFactory factory,
            ApplicationConfiguration configuration,
            bool replacement,
            Func<IAsyncNodeManager, bool> isRegistered,
            CancellationToken cancellationToken)
        {
            return CreateNodeManagerAsync(
                factory, configuration, replacement, isRegistered, isRegistered, cancellationToken);
        }

        /// <summary>
        /// Invokes a candidate factory and distinguishes exact facades from borrowed resources.
        /// </summary>
        internal async ValueTask<IAsyncNodeManager> CreateNodeManagerAsync(
            IAsyncNodeManagerFactory factory,
            ApplicationConfiguration configuration,
            bool replacement,
            Func<IAsyncNodeManager, bool> isRegistered,
            Func<IAsyncNodeManager, bool> isExactlyRegistered,
            CancellationToken cancellationToken)
        {
            using var server = new StagingServer(
                this,
                factory is INodeManagerBorrowedResourceFactory,
                isRegistered,
                isExactlyRegistered);
            BorrowedNodeManagerLeaseState? borrowed = null;
            IAsyncNodeManager manager;
            try
            {
                if (factory is INodeManagerBorrowedResourceFactory borrowedFactory)
                {
                    try
                    {
                        await borrowedFactory.CreateBorrowedAsync(server, configuration, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        await server.CloseBorrowedFactoryAsync().ConfigureAwait(false);
                    }
                    server.ThrowBorrowedDeclarationFailure();
                    borrowed = server.BorrowedLease ??
                        throw new InvalidOperationException("The borrowed NodeManager factory declared no lease.");
                    manager = server.GetDeclaredBorrowedCandidate();
                    if (!ReferenceEquals(borrowed.Lease.NodeManager, manager))
                    {
                        throw new InvalidOperationException(
                            "The borrowed NodeManager lease changed its declared candidate.");
                    }
                }
                else
                {
                    manager = await factory.CreateAsync(server, configuration, cancellationToken)
                        .ConfigureAwait(false) ??
                        throw new InvalidOperationException(replacement
                            ? "The replacement NodeManager factory returned null."
                            : "The NodeManager factory returned null.");
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                borrowed ??= server.BorrowedLease;
                Exception? cleanupFailure = await CleanupFailedCreationAsync(null, borrowed, server, false)
                    .ConfigureAwait(false);
                if (cleanupFailure is not null)
                {
                    throw new AggregateException(
                        "Candidate creation and cleanup both failed.",
                        failure,
                        cleanupFailure);
                }
                throw;
            }

            bool ownsManager = false;
            try
            {
                if (borrowed is null &&
                    HasKnownBorrowedResource(manager, server, isRegistered, isExactlyRegistered))
                {
                    throw new NodeManagerBorrowedResourceException(manager);
                }
                if (borrowed is not null)
                {
                    if (isExactlyRegistered(manager))
                    {
                        throw new NodeManagerAlreadyRegisteredException();
                    }
                    IAsyncNodeManager resource = MasterNodeManager.GetReferenceOwnerIdentity(manager);
                    if (ReferenceEquals(manager, resource))
                    {
                        StagingServer? previousServer = GetServer(manager);
                        if (isRegistered(manager))
                        {
                            throw new NodeManagerAlreadyRegisteredException();
                        }
                        if ((previousServer is { IsDetached: false } &&
                                !ReferenceEquals(previousServer, server)) ||
                            HasKnownBorrowedResource(manager, server, isRegistered, isExactlyRegistered))
                        {
                            throw new InvalidOperationException(
                                "A borrowed live NodeManager requires a distinct facade for its candidate.");
                        }
                    }
                    if (!ReferenceEquals(s_borrowedCandidates.GetValue(manager, _ => server), server))
                    {
                        throw new NodeManagerAlreadyRegisteredException();
                    }
                }
                else
                {
                    if (isRegistered(manager))
                    {
                        throw new NodeManagerAlreadyRegisteredException();
                    }
                    IAsyncNodeManager identity = NodeManagerGenerationRegistry.GetIdentity(manager);
                    if (!ReferenceEquals(s_candidates.GetValue(identity, _ => server), server))
                    {
                        throw new NodeManagerAlreadyRegisteredException();
                    }
                }
                if (borrowed is not null && isRegistered(manager))
                {
                    throw new NodeManagerAlreadyRegisteredException();
                }
                server.CompleteFactory(manager);
                ownsManager = true;
                ValidateHistorianReadiness(manager);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                Exception? cleanupFailure = await CleanupFailedCreationAsync(manager, borrowed, server, ownsManager)
                    .ConfigureAwait(false);
                if (cleanupFailure is not null)
                {
                    throw new AggregateException(
                        "Candidate validation and cleanup both failed.",
                        failure,
                        cleanupFailure);
                }
                throw;
            }
            return manager;
        }

        /// <summary>
        /// Detects a resource already associated with another candidate or a live generation.
        /// </summary>
        private static bool HasKnownBorrowedResource(
            IAsyncNodeManager manager,
            StagingServer candidate,
            Func<IAsyncNodeManager, bool> isRegistered,
            Func<IAsyncNodeManager, bool> isExactlyRegistered)
        {
            IAsyncNodeManager resource = MasterNodeManager.GetReferenceOwnerIdentity(manager);
            if (ReferenceEquals(manager, resource))
            {
                StagingServer? directServer = GetServer(resource);
                if (ReferenceEquals(directServer, candidate))
                {
                    return false;
                }
                if (directServer is { IsDetached: false } ||
                    isRegistered(resource) ||
                    isExactlyRegistered(resource))
                {
                    return false;
                }
                if (NodeManagerGenerationRegistry.TryGet(resource, out NodeManagerGeneration? direct) &&
                    direct is not null &&
                    !candidate.OwnsProvisionalSource(
                        NodeManagerGenerationRegistry.GetIdentity(resource), direct))
                {
                    return true;
                }
                IServerInternal? directOwner = GetKnownResourceServer(resource);
                return directOwner is not null && !ReferenceEquals(directOwner, candidate);
            }
            StagingServer? resourceServer = GetServer(resource);
            if (ReferenceEquals(resourceServer, candidate))
            {
                return false;
            }
            if (resourceServer is not null && !resourceServer.IsDetached)
            {
                return true;
            }
            if (NodeManagerGenerationRegistry.TryGet(resource, out NodeManagerGeneration? existing) &&
                existing is not null &&
                !candidate.OwnsProvisionalSource(
                    NodeManagerGenerationRegistry.GetIdentity(resource), existing))
            {
                return true;
            }
            if (isRegistered(resource) || isExactlyRegistered(resource))
            {
                return true;
            }
            IServerInternal? resourceOwner = GetKnownResourceServer(resource);
            return resourceOwner is not null && !ReferenceEquals(resourceOwner, candidate);
        }

        /// <summary>
        /// Gets the server exposed by a built-in resource manager, if available.
        /// </summary>
        /// <param name="resource">The physical resource manager.</param>
        /// <returns>The resource's server, or null when it does not expose one.</returns>
        internal static IServerInternal? GetKnownResourceServer(IAsyncNodeManager resource)
        {
            return resource is AsyncCustomNodeManager asynchronous
                ? asynchronous.Server
                : (resource.SyncNodeManager as CustomNodeManager2)?.Server;
        }

        /// <summary>
        /// Releases candidate-owned state after creation or validation fails.
        /// </summary>
        private static async ValueTask<Exception?> CleanupFailedCreationAsync(
            IAsyncNodeManager? manager,
            BorrowedNodeManagerLeaseState? borrowed,
            StagingServer server,
            bool ownsManager)
        {
            List<Exception>? failures = null;
            try
            {
                if (borrowed is not null)
                {
                    await borrowed.RollbackAsync().ConfigureAwait(false);
                }
                else if (ownsManager && manager is not null)
                {
                    await DisposeCreatedManagerAsync(manager).ConfigureAwait(false);
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                failures = [failure];
            }
            if (borrowed is null)
            {
                try
                {
                    await server.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    (failures ??= []).Add(failure);
                }
            }
            return failures switch
            {
                null => null,
                { Count: 1 } => failures[0],
                _ => new AggregateException("Candidate cleanup failed.", failures)
            };
        }

        /// <summary>
        /// Rejects staged historian state that cannot be published through a readiness callback.
        /// </summary>
        internal static void ValidateHistorianReadiness(IAsyncNodeManager manager)
        {
            StagingServer? server = GetServer(manager);
            if (server?.HasPendingHistorianState != true)
            {
                return;
            }
            if (!server.CanPublishHistorianRegistrations)
            {
                throw new NotSupportedException(
                    "The serving historian registry does not support candidate-owned registrations.");
            }
            if (!NodeManagerReadiness.IsParticipant(manager))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Historian registration during preparation requires {0} to implement " +
                    "INodeManagerReadinessNotification.OnServerReadyAsync.",
                    manager.GetType().FullName ?? "(unknown)");
            }
        }

        private static async ValueTask DisposeCreatedManagerAsync(IAsyncNodeManager manager)
        {
            if (manager is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (manager is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        /// <summary>
        /// Gets the candidate's explicit staging server, if it has one.
        /// </summary>
        internal static StagingServer? GetServer(IAsyncNodeManager manager)
        {
            if (s_borrowedCandidates.TryGetValue(manager, out StagingServer? borrowedServer))
            {
                return borrowedServer;
            }
            IAsyncNodeManager identity = NodeManagerGenerationRegistry.GetIdentity(manager);
            if (s_candidates.TryGetValue(identity, out StagingServer? server))
            {
                return server;
            }
            return identity is AsyncCustomNodeManager asynchronous
                ? asynchronous.Server as StagingServer
                : (identity.SyncNodeManager as CustomNodeManager2)?.Server as StagingServer;
        }

        /// <summary>
        /// Gets the cleanup state for an exact borrowed candidate.
        /// </summary>
        internal static BorrowedNodeManagerLeaseState? GetBorrowedLease(IAsyncNodeManager manager)
        {
            return s_borrowedCandidates.TryGetValue(manager, out StagingServer? server)
                ? server.BorrowedLease
                : null;
        }

        /// <summary>
        /// Determines whether the exact facade owns a borrowed-resource lease.
        /// </summary>
        internal static bool IsBorrowedCandidate(IAsyncNodeManager manager)
        {
            return s_borrowedCandidates.TryGetValue(manager, out _);
        }

        /// <summary>
        /// Determines whether the candidate server declared the exact borrowed facade.
        /// </summary>
        internal static bool IsBorrowedCandidateOf(IAsyncNodeManager manager, StagingServer server)
        {
            return s_borrowedCandidates.TryGetValue(manager, out StagingServer? current) &&
                ReferenceEquals(current, server);
        }

        /// <summary>
        /// Associates an exact borrowed facade with its candidate server.
        /// </summary>
        internal static bool TryAssociateBorrowedCandidate(IAsyncNodeManager manager, StagingServer server)
        {
            return ReferenceEquals(s_borrowedCandidates.GetValue(manager, _ => server), server);
        }

        /// <summary>
        /// Removes a borrowed facade association after its declaration fails.
        /// </summary>
        internal static void RemoveBorrowedCandidate(IAsyncNodeManager manager, StagingServer server)
        {
            if (s_borrowedCandidates.TryGetValue(manager, out StagingServer? current) &&
                ReferenceEquals(current, server))
            {
                s_borrowedCandidates.Remove(manager);
            }
        }

        /// <summary>
        /// Reserves one lease until its candidate is accepted or the declaration is rejected.
        /// </summary>
        private static BorrowedOwnershipClaim ClaimBorrowedLease(INodeManagerBorrowedResourceLease lease)
        {
            lock (s_borrowedOwnershipLock)
            {
                if (s_borrowedLeaseClaims.TryGetValue(lease, out _))
                {
                    throw new NodeManagerAlreadyRegisteredException(
                        "The borrowed NodeManager lease is already owned by another candidate.");
                }
                var claim = new BorrowedOwnershipClaim();
                s_borrowedLeaseClaims.Add(lease, claim);
                return claim;
            }
        }

        /// <summary>
        /// Reserves the exact facade without transferring cleanup for an existing candidate.
        /// </summary>
        private static void ClaimBorrowedFacade(IAsyncNodeManager manager, BorrowedOwnershipClaim claim)
        {
            lock (s_borrowedOwnershipLock)
            {
                if (s_borrowedFacadeClaims.TryGetValue(manager, out _) ||
                    s_borrowedCandidates.TryGetValue(manager, out _))
                {
                    throw new NodeManagerAlreadyRegisteredException(
                        "The borrowed NodeManager candidate is already owned by another lease.");
                }
                s_borrowedFacadeClaims.Add(manager, claim);
            }
        }

        /// <summary>
        /// Releases reservations when declaration fails before cleanup ownership transfers.
        /// </summary>
        private static void ReleaseUnacceptedBorrowedClaims(
            INodeManagerBorrowedResourceLease lease,
            IAsyncNodeManager? manager,
            BorrowedOwnershipClaim claim)
        {
            lock (s_borrowedOwnershipLock)
            {
                if (s_borrowedLeaseClaims.TryGetValue(lease, out BorrowedOwnershipClaim? leaseClaim) &&
                    ReferenceEquals(leaseClaim, claim))
                {
                    s_borrowedLeaseClaims.Remove(lease);
                }
                if (manager is not null &&
                    s_borrowedFacadeClaims.TryGetValue(manager, out BorrowedOwnershipClaim? facadeClaim) &&
                    ReferenceEquals(facadeClaim, claim))
                {
                    s_borrowedFacadeClaims.Remove(manager);
                }
            }
        }

        /// <summary>
        /// Publishes canonical storage into all cached handles of this candidate.
        /// </summary>
        internal static void Bind(IAsyncNodeManager manager)
        {
            GetServer(manager)?.Bind();
        }

        /// <summary>
        /// Detaches candidate metadata before deletion or disposal can remove serving registrations.
        /// </summary>
        internal static void Detach(IAsyncNodeManager manager)
        {
            GetServer(manager)?.Detach();
        }

        /// <summary>
        /// Publishes candidate historian registrations after its readiness callback succeeds.
        /// </summary>
        /// <param name="manager">The ready NodeManager.</param>
        internal static bool PublishHistorianRegistrations(IAsyncNodeManager manager)
        {
            return GetServer(manager)?.PublishHistorianRegistrations() == true;
        }

        /// <summary>
        /// Withdraws candidate historian registrations when its generation leaves routing.
        /// </summary>
        /// <param name="manager">The retired NodeManager.</param>
        internal static bool RetireHistorianRegistrations(IAsyncNodeManager manager)
        {
            return GetServer(manager)?.RetireHistorianRegistrations() == true;
        }

        /// <summary>
        /// Disposes candidate-owned historian builders and staged provider state.
        /// </summary>
        /// <param name="manager">The candidate NodeManager.</param>
        /// <returns>The asynchronous cleanup.</returns>
        internal static ValueTask DisposeAsync(IAsyncNodeManager manager)
        {
            return GetServer(manager)?.DisposeAsync() ?? default;
        }

        /// <summary>
        /// Revokes a manager's service-level writer as soon as its generation leaves routing.
        /// </summary>
        /// <param name="manager">The retiring NodeManager.</param>
        internal static void RevokeServiceLevelControl(IAsyncNodeManager manager)
        {
            GetServer(manager)?.RevokeServiceLevelControl();
        }

        /// <summary>Retains candidate-owned handles without extending manager lifetimes.</summary>
        private static readonly ConditionalWeakTable<IAsyncNodeManager, StagingServer> s_candidates = new();

        /// <summary>Associates a leased facade without replacing its borrowed resource's staging owner.</summary>
        private static readonly ConditionalWeakTable<IAsyncNodeManager, StagingServer> s_borrowedCandidates = new();

        /// <summary>Serializes reservations of lease and facade identities.</summary>
        private static readonly Lock s_borrowedOwnershipLock = new();

        /// <summary>Retains accepted lease identities while their keys remain reachable.</summary>
        private static readonly ConditionalWeakTable<INodeManagerBorrowedResourceLease, BorrowedOwnershipClaim>
            s_borrowedLeaseClaims = new();

        /// <summary>Retains accepted facade identities while their keys remain reachable.</summary>
        private static readonly ConditionalWeakTable<IAsyncNodeManager, BorrowedOwnershipClaim>
            s_borrowedFacadeClaims = new();

        /// <summary>
        /// Identifies one reservation without retaining the lease, facade, or staging server.
        /// </summary>
        private sealed class BorrowedOwnershipClaim
        {
        }

        /// <summary>
        /// Candidate-specific server facade. Metadata handles are stable even when copied into a system context.
        /// </summary>
        internal sealed class StagingServer :
            IServerInternal, INodeIdFactoryProvider, ITimeProviderProvider,
            INodeManagerBorrowedResourcePreparation,
            IHistorianRegistryProvider, IHistorianBuilderRegistry,
            AliasNames.IAliasNameStoreRegistryProvider, AliasNames.IAliasNameStoreRegistrationValidator,
            IConformanceUnitsManagerProvider, IServerEndpointRegistryProvider, ITransportListenerRegistryProvider,
            ISecurityPolicyRegistryProvider, ICertificateValidatorProvider, IHistoryContinuationPointStoreProvider,
            IAsyncDisposable,
            IServerServiceLevelControl
        {
            /// <summary>
            /// Creates a candidate's metadata handles over the preparation's shared private storage.
            /// </summary>
            internal StagingServer(
                ChangePreparer preparation,
                bool usesBorrowedFactory = false,
                Func<IAsyncNodeManager, bool>? isRegistered = null,
                Func<IAsyncNodeManager, bool>? isExactlyRegistered = null)
            {
                m_server = preparation.Server;
                UsesBorrowedFactory = usesBorrowedFactory;
                m_isRegistered = isRegistered;
                m_isExactlyRegistered = isExactlyRegistered;
                m_servingNamespaceUris = m_server.NamespaceUris;
                m_servingServerUris = m_server.ServerUris;
                m_servingTypes = preparation.OriginalTypes;
                m_servingFactory = preparation.OriginalFactory;
                NamespaceUris = new NamespaceTable();
                NamespaceUris.BindTo(preparation.NamespaceUris);
                ServerUris = new StringTable();
                ServerUris.BindTo(preparation.ServerUris);
                TypeTree = new TypeTable(NamespaceUris);
                TypeTree.BindTo(preparation.TypeTree);
                TypeTree.RejectCandidateConflicts();
                Factory = preparation.Factory.Fork();
                ((EncodeableFactory)Factory).BindTo(preparation.Factory);
                DefaultSystemContext = new ServerSystemContext(this).Copy(m_server.DefaultSystemContext);
                DefaultSystemContext.NamespaceUris = NamespaceUris;
                DefaultSystemContext.ServerUris = ServerUris;
                DefaultSystemContext.TypeTable = TypeTree;
                DefaultSystemContext.EncodeableFactory = Factory;
                MessageContext = new ServiceMessageContext(m_server.Telemetry, Factory)
                {
                    NamespaceUris = NamespaceUris,
                    ServerUris = ServerUris,
                    MaxStringLength = m_server.MessageContext.MaxStringLength,
                    MaxByteStringLength = m_server.MessageContext.MaxByteStringLength,
                    MaxArrayLength = m_server.MessageContext.MaxArrayLength,
                    MaxMessageSize = m_server.MessageContext.MaxMessageSize,
                    MaxEncodingNestingLevels = m_server.MessageContext.MaxEncodingNestingLevels,
                    MaxDecoderRecoveries = m_server.MessageContext.MaxDecoderRecoveries
                };
                m_historianRegistry = m_server is IHistorianRegistryProvider historianRegistryProvider
                    ? new StagedHistorianProviderRegistry(
                        NamespaceUris,
                        historianRegistryProvider.HistorianRegistry,
                        this)
                    : null;
            }

            /// <inheritdoc/>
            public NamespaceTable NamespaceUris { get; }

            /// <inheritdoc/>
            IServerInternal INodeManagerBorrowedResourcePreparation.Server => this;

            /// <inheritdoc/>
            public StringTable ServerUris { get; }

            /// <inheritdoc/>
            public TypeTable TypeTree { get; }

            /// <inheritdoc/>
            public IEncodeableFactory Factory { get; }

            /// <inheritdoc/>
            public ServerSystemContext DefaultSystemContext { get; }

            /// <inheritdoc/>
            public IServiceMessageContext MessageContext { get; }

            /// <inheritdoc/>
            public TimeProvider TimeProvider => (m_server as ITimeProviderProvider)?.TimeProvider ?? TimeProvider.System;

            /// <inheritdoc/>
            public IRebasableNodeIdFactory? NodeIdFactory => (m_server as INodeIdFactoryProvider)?.NodeIdFactory;

            /// <inheritdoc/>
            public bool? DetectNodeIdCollisions => (m_server as INodeIdFactoryProvider)?.DetectNodeIdCollisions;

            /// <inheritdoc/>
            public ArrayOf<EndpointDescription> ServerEndpoints =>
                m_server is IServerEndpointRegistryProvider provider ? provider.ServerEndpoints : [];

            /// <inheritdoc/>
            public IReadOnlyList<ITransportListener> TransportListeners =>
                (m_server as ITransportListenerRegistryProvider)?.TransportListeners ?? [];

            /// <inheritdoc/>
            public ISecurityPolicyRegistry SecurityPolicyRegistry =>
                (m_server as ISecurityPolicyRegistryProvider)?.SecurityPolicyRegistry ?? SecurityPolicies.Default;

            /// <inheritdoc/>
            public ICertificateValidatorEx? CertificateValidator =>
                (m_server as ICertificateValidatorProvider)?.CertificateValidator;

            /// <inheritdoc/>
            public IHistoryContinuationPointStore? HistoryContinuationPointStore =>
                (m_server as IHistoryContinuationPointStoreProvider)?.HistoryContinuationPointStore;

            /// <inheritdoc/>
            public IEnumerable<Uri> EndpointAddresses => m_server.EndpointAddresses;

            /// <inheritdoc/>
            public IMainNodeManagerFactory MainNodeManagerFactory => m_server.MainNodeManagerFactory;

            /// <inheritdoc/>
            public IMasterNodeManager NodeManager => m_server.NodeManager;

            /// <inheritdoc/>
            public ICoreNodeManager CoreNodeManager => m_server.CoreNodeManager;

            /// <inheritdoc/>
            public IDiagnosticsNodeManager DiagnosticsNodeManager => m_server.DiagnosticsNodeManager;

            /// <inheritdoc/>
            public IConfigurationNodeManager ConfigurationNodeManager => m_server.ConfigurationNodeManager;

            /// <inheritdoc/>
            public EventManager EventManager => m_server.EventManager;

            /// <inheritdoc/>
            public ResourceManager ResourceManager => m_server.ResourceManager;

            /// <inheritdoc/>
            public RequestManager RequestManager => m_server.RequestManager;

            /// <inheritdoc/>
            public AggregateManager AggregateManager => m_server.AggregateManager;

            /// <inheritdoc/>
            public ConformanceUnitsManager? ConformanceUnitsManager => IsStaging || IsDetached
                ? null
                : (m_server as IConformanceUnitsManagerProvider)?.ConformanceUnitsManager;

            /// <inheritdoc/>
            public ISessionManager SessionManager => m_server.SessionManager;

            /// <inheritdoc/>
            public IRoleManager RoleManager => m_server.RoleManager;

            /// <inheritdoc/>
            public IServerIdentityRegistry IdentityRegistry => m_server.IdentityRegistry;

            /// <inheritdoc/>
            public UserManagement.IUserManagement? UserManagement => m_server.UserManagement;

            /// <inheritdoc/>
            public IHistorianProviderRegistry HistorianRegistry
            {
                get
                {
                    return m_historianRegistry ??
                        throw new NotSupportedException("The server does not provide a historian registry.");
                }
            }

            /// <inheritdoc/>
            void IHistorianBuilderRegistry.RegisterHistorianBuilder(HistorianBuilder builder)
            {
                if (builder == null)
                {
                    throw new ArgumentNullException(nameof(builder));
                }
                lock (m_historianBuildersLock)
                {
                    if (IsDetached || m_historianBuildersDisposed)
                    {
                        throw new ObjectDisposedException(nameof(StagingServer));
                    }
                    if (m_server is not IHistorianBuilderRegistry ||
                        m_historianRegistry?.SupportsOwnedRegistrations != true)
                    {
                        throw new NotSupportedException(
                            "The serving server cannot manage candidate historian builder lifetimes.");
                    }
                    if (!m_historianBuilders.Contains(builder))
                    {
                        m_historianBuilders.Add(builder);
                    }
                }
            }

            /// <inheritdoc/>
            void IHistorianBuilderRegistry.UnregisterHistorianBuilder(HistorianBuilder builder)
            {
                if (builder == null)
                {
                    throw new ArgumentNullException(nameof(builder));
                }
                lock (m_historianBuildersLock)
                {
                    m_historianBuilders.Remove(builder);
                }
            }

            /// <inheritdoc/>
            public AliasNames.IAliasNameStoreRegistry AliasNameStoreRegistry
            {
                get
                {
                    EnsurePublishedRegistryAccess();
                    return m_server is AliasNames.IAliasNameStoreRegistryProvider provider
                        ? provider.AliasNameStoreRegistry
                        : throw new NotSupportedException("The server does not provide an alias-name registry.");
                }
            }

            /// <inheritdoc />
            public void ValidateAliasNameStoreRegistration(
                AliasNames.IAliasNameStore store, bool replacesRegisteredStore)
            {
                if (m_server is AliasNames.IAliasNameStoreRegistryProvider provider)
                {
                    AliasNames.AliasNameNodeManager.ValidateRegistryRegistration(
                        provider.AliasNameStoreRegistry, store, replacesRegisteredStore);
                }
            }

            /// <inheritdoc/>
            public ISubscriptionManager SubscriptionManager => m_server.SubscriptionManager;

            /// <inheritdoc/>
            public IMonitoredItemQueueFactory MonitoredItemQueueFactory => m_server.MonitoredItemQueueFactory;

            /// <inheritdoc/>
            public ISubscriptionStore SubscriptionStore => m_server.SubscriptionStore;

            /// <inheritdoc/>
            public ITelemetryContext Telemetry => m_server.Telemetry;

            /// <inheritdoc/>
            public bool IsRunning => m_server.IsRunning;

            /// <inheritdoc/>
            public ServerState CurrentState => m_server.CurrentState;

            /// <inheritdoc/>
            public ServerObjectState ServerObject => m_server.ServerObject;

            /// <inheritdoc/>
            public bool Auditing => m_server.Auditing;

            /// <inheritdoc/>
            public ISystemContext DefaultAuditContext => DefaultSystemContext;

            /// <summary>
            /// Gets whether this candidate's metadata is still private.
            /// </summary>
            internal bool IsStaging => Volatile.Read(ref m_isStaging);

            /// <summary>
            /// Gets whether the factory declared borrowed-resource ownership before creating its candidate.
            /// </summary>
            internal bool UsesBorrowedFactory { get; }

            /// <summary>
            /// Gets the candidate's borrowed-resource cleanup state.
            /// </summary>
            internal BorrowedNodeManagerLeaseState? BorrowedLease => Volatile.Read(ref m_borrowedLease);

            /// <summary>
            /// Stops declarations and waits for one already in progress.
            /// </summary>
            internal async ValueTask CloseBorrowedFactoryAsync()
            {
                Task? pending;
                lock (m_borrowedDeclarationLock)
                {
                    m_borrowedFactoryClosed = true;
                    pending = m_borrowedDeclaration?.Task;
                }
                if (pending is not null)
                {
                    await pending.ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Gets the declared facade after factory completion.
            /// </summary>
            internal IAsyncNodeManager GetDeclaredBorrowedCandidate()
            {
                ThrowBorrowedDeclarationFailure();
                return Volatile.Read(ref m_declaredBorrowedCandidate) ??
                    throw new InvalidOperationException("The borrowed NodeManager factory declared no candidate.");
            }

            /// <summary>
            /// Preserves a declaration failure even when the factory catches it.
            /// </summary>
            internal void ThrowBorrowedDeclarationFailure()
            {
                Volatile.Read(ref m_borrowedDeclarationFailure)?.Throw();
            }

            /// <summary>
            /// Rejects a candidate identity that belongs to another registration.
            /// </summary>
            private void ValidateBorrowedCandidateIdentity(IAsyncNodeManager manager, IAsyncNodeManager resource)
            {
                Func<IAsyncNodeManager, bool> isRegistered = m_isRegistered ??
                    throw new InvalidOperationException("The candidate has no registration lookup.");
                Func<IAsyncNodeManager, bool> isExactlyRegistered = m_isExactlyRegistered ??
                    throw new InvalidOperationException("The candidate has no exact registration lookup.");
                if (isExactlyRegistered(manager))
                {
                    throw new NodeManagerAlreadyRegisteredException();
                }
                if (ReferenceEquals(manager, resource))
                {
                    StagingServer? previousServer = GetServer(manager);
                    if (isRegistered(manager))
                    {
                        throw new NodeManagerAlreadyRegisteredException();
                    }
                    if ((previousServer is { IsDetached: false } && !ReferenceEquals(previousServer, this)) ||
                        HasKnownBorrowedResource(manager, this, isRegistered, isExactlyRegistered))
                    {
                        throw new InvalidOperationException(
                            "A borrowed live NodeManager requires a distinct facade for its candidate.");
                    }
                }
            }

            /// <summary>
            /// Validates a borrowed resource before associating its facade with this server.
            /// </summary>
            private void ValidateBorrowedCandidate(IAsyncNodeManager manager, IAsyncNodeManager resource)
            {
                ValidateBorrowedCandidateIdentity(manager, resource);
                IServerInternal? owner = GetKnownResourceServer(resource);
                while (owner is StagingServer stagingOwner)
                {
                    owner = stagingOwner.m_server;
                }
                IServerInternal serving = m_server;
                while (serving is StagingServer stagingServer)
                {
                    serving = stagingServer.m_server;
                }
                if (owner is not null && !ReferenceEquals(owner, serving))
                {
                    throw new NotSupportedException(
                        "A borrowed NodeManager resource belongs to another server.");
                }
            }

            /// <summary>
            /// Gets or sets the exact candidate facade returned by the factory.
            /// </summary>
            internal IAsyncNodeManager? RegisteredFacade
            {
                get => Volatile.Read(ref m_registeredFacade);
                set => Volatile.Write(ref m_registeredFacade, value);
            }

            /// <inheritdoc/>
            public void DeclareCandidate(INodeManagerBorrowedResourceLease lease)
            {
                if (lease is null)
                {
                    throw new ArgumentNullException(nameof(lease));
                }

                TaskCompletionSource<bool> completion;
                lock (m_borrowedDeclarationLock)
                {
                    if (!UsesBorrowedFactory || m_borrowedFactoryClosed || IsDetached)
                    {
                        throw new InvalidOperationException("The borrowed factory no longer accepts a candidate.");
                    }
                    if (m_borrowedDeclaration is not null)
                    {
                        throw new InvalidOperationException("The borrowed factory already declared a candidate.");
                    }
                    m_borrowedDeclaration = completion = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }

                BorrowedOwnershipClaim? claim = null;
                IAsyncNodeManager? manager = null;
                bool accepted = false;
                try
                {
                    claim = ClaimBorrowedLease(lease);
                    manager = lease.NodeManager ??
                        throw new InvalidOperationException("The borrowed NodeManager lease returned no candidate.");
                    IAsyncNodeManager resource = MasterNodeManager.GetReferenceOwnerIdentity(manager);
                    ValidateBorrowedCandidateIdentity(manager, resource);
                    ClaimBorrowedFacade(manager, claim);
                    var state = new BorrowedNodeManagerLeaseState(lease, this);
                    state.SetCandidate(manager);
                    Volatile.Write(ref m_borrowedLease, state);
                    accepted = true;
                    ValidateBorrowedCandidate(manager, resource);
                    NodeManagerGenerationRegistry.DeclareBorrowedCandidate(this, manager, resource);
                    Volatile.Write(ref m_declaredBorrowedCandidate, manager);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    lock (m_borrowedDeclarationLock)
                    {
                        m_borrowedDeclarationFailure = ExceptionDispatchInfo.Capture(failure);
                    }
                    throw;
                }
                finally
                {
                    if (!accepted && claim is not null)
                    {
                        ReleaseUnacceptedBorrowedClaims(lease, manager, claim);
                    }
                    completion.TrySetResult(true);
                }
            }

            /// <summary>
            /// Creates an initial source generation using the private resource's shared gate.
            /// </summary>
            /// <param name="identity">The source registry key.</param>
            /// <param name="resource">The physical resource behind the source.</param>
            /// <param name="manager">The exact source manager.</param>
            /// <returns>The new provisional source generation.</returns>
            internal NodeManagerGeneration CreateProvisionalSource(
                IAsyncNodeManager identity,
                IAsyncNodeManager resource,
                IAsyncNodeManager manager)
            {
                lock (m_provisionalSourcesLock)
                {
                    if (IsDetached ||
                        (m_factoryCompleted &&
                            !ReferenceEquals(m_sourceIdentity, identity) &&
                            !ReferenceEquals(m_facadeIdentity, identity)))
                    {
                        throw new InvalidOperationException(
                            "The staging server no longer accepts this source manager.");
                    }
                    GenerationGate? gate = null;
                    foreach (var source in m_provisionalSources)
                    {
                        if (ReferenceEquals(source.Resource, resource))
                        {
                            gate = source.Generation.Gate;
                            break;
                        }
                    }
                    var generation = new NodeManagerGeneration(
                        new NodeManagerSlot(),
                        1,
                        manager,
                        NodeManagerGenerationState.Prepared,
                        privateGate: gate);
                    m_provisionalSources.Add((identity, resource, generation));
                    return generation;
                }
            }

            /// <summary>
            /// Determines whether this candidate created the exact provisional source.
            /// </summary>
            /// <param name="identity">The source registry key.</param>
            /// <param name="generation">The source generation.</param>
            /// <returns>Whether the source belongs to this candidate.</returns>
            internal bool OwnsProvisionalSource(
                IAsyncNodeManager identity,
                NodeManagerGeneration generation)
            {
                lock (m_provisionalSourcesLock)
                {
                    return m_provisionalSources.Exists(source =>
                        ReferenceEquals(source.Identity, identity) &&
                        ReferenceEquals(source.Generation, generation));
                }
            }

            /// <summary>
            /// Retains the selected resource's private bindings and releases unrelated provisional identities.
            /// </summary>
            /// <param name="manager">The facade returned by the factory.</param>
            internal void CompleteFactory(IAsyncNodeManager manager)
            {
                IAsyncNodeManager source = NodeManagerGenerationRegistry.GetIdentity(
                    MasterNodeManager.GetReferenceOwnerIdentity(manager));
                IAsyncNodeManager facade = NodeManagerGenerationRegistry.GetIdentity(manager);
                List<(IAsyncNodeManager Identity, NodeManagerGeneration Generation, bool AbortGate)> released;
                lock (m_provisionalSourcesLock)
                {
                    if (IsDetached)
                    {
                        throw new InvalidOperationException("The staging server has been detached.");
                    }
                    m_factoryCompleted = true;
                    m_sourceIdentity = source;
                    m_facadeIdentity = facade;
                    RegisteredFacade = manager;
                    var retainedGates = new HashSet<GenerationGate>();
                    foreach (var entry in m_provisionalSources)
                    {
                        if (ReferenceEquals(entry.Resource, source) || ReferenceEquals(entry.Identity, facade))
                        {
                            retainedGates.Add(entry.Generation.Gate);
                        }
                    }
                    released = [];
                    foreach (var entry in m_provisionalSources)
                    {
                        if (!ReferenceEquals(entry.Resource, source) && !ReferenceEquals(entry.Identity, facade))
                        {
                            released.Add((
                                entry.Identity,
                                entry.Generation,
                                !retainedGates.Contains(entry.Generation.Gate)));
                        }
                    }
                    m_provisionalSources.RemoveAll(entry =>
                        !ReferenceEquals(entry.Resource, source) && !ReferenceEquals(entry.Identity, facade));
                }
                foreach ((IAsyncNodeManager identity, NodeManagerGeneration generation, bool abortGate) in released)
                {
                    NodeManagerGenerationRegistry.ReleaseProvisionalSource(identity, generation, abortGate);
                }
            }

            /// <summary>
            /// Releases the provisional source identities still owned by this candidate.
            /// </summary>
            private void ReleaseProvisionalSources()
            {
                List<(
                    IAsyncNodeManager Identity,
                    IAsyncNodeManager Resource,
                    NodeManagerGeneration Generation)> released;
                lock (m_provisionalSourcesLock)
                {
                    m_factoryCompleted = true;
                    m_sourceIdentity = null;
                    m_facadeIdentity = null;
                    released = [.. m_provisionalSources];
                    m_provisionalSources.Clear();
                }
                foreach (var entry in released)
                {
                    NodeManagerGenerationRegistry.ReleaseProvisionalSource(entry.Identity, entry.Generation);
                }
            }

            /// <summary>
            /// Gets whether this facade has been detached from its published generation.
            /// </summary>
            internal bool IsDetached => Volatile.Read(ref m_isDetached);

            /// <summary>
            /// Gets whether preparation recorded candidate-owned historian bindings that require readiness.
            /// </summary>
            internal bool HasPendingHistorianState => m_historianRegistry?.HasRegistrations == true;

            /// <summary>
            /// Gets whether the serving historian registry supports owner-scoped publication.
            /// </summary>
            internal bool CanPublishHistorianRegistrations =>
                m_historianRegistry?.SupportsOwnedRegistrations == true;

            /// <summary>
            /// Publishes candidate historian bindings and transfers builder tracking to the server.
            /// </summary>
            internal bool PublishHistorianRegistrations()
            {
                EnsurePublishedRegistryAccess();
                HistorianBuilder[] builders;
                lock (m_historianBuildersLock)
                {
                    builders = [.. m_historianBuilders];
                }
                if (builders.Length != 0 && m_server is not IHistorianBuilderRegistry)
                {
                    throw new NotSupportedException("The server does not manage historian builder lifetimes.");
                }

                m_historianRegistry?.Publish();
                if (m_server is IHistorianBuilderRegistry registry)
                {
                    foreach (HistorianBuilder builder in builders)
                    {
                        registry.RegisterHistorianBuilder(builder);
                    }
                }
                return m_historianRegistry?.HasRegistrations == true;
            }

            /// <summary>
            /// Withdraws this candidate's bindings from the serving registry.
            /// </summary>
            internal bool RetireHistorianRegistrations()
            {
                return m_historianRegistry?.Retire() == true;
            }

            /// <summary>
            /// Gets explicit namespace registrations retained until routing publication.
            /// </summary>
            internal IEnumerable<string> NamespaceRegistrations => m_pendingNamespaces;

            /// <inheritdoc/>
            public void Dispose()
            {
                // The facade does not own the running server.
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                Volatile.Write(ref m_registeredFacade, null);
                HistorianBuilder[] builders;
                bool alreadyDisposed;
                lock (m_historianBuildersLock)
                {
                    alreadyDisposed = m_historianBuildersDisposed;
                    builders = alreadyDisposed ? [] : [.. m_historianBuilders];
                }
                if (alreadyDisposed)
                {
                    ReleaseProvisionalSources();
                    ReleaseAbandonedReferences();
                    return;
                }

                List<Exception>? failures = null;
                try
                {
                    RetireHistorianRegistrations();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failures = [exception];
                }

                foreach (HistorianBuilder builder in builders)
                {
                    bool cleanupCompleted = true;
                    try
                    {
                        await builder.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        cleanupCompleted = false;
                        failures ??= [];
                        failures.Add(exception);
                    }
                    if (m_server is IHistorianBuilderRegistry registry)
                    {
                        try
                        {
                            registry.UnregisterHistorianBuilder(builder);
                        }
                        catch (Exception exception) when (exception is not OutOfMemoryException)
                        {
                            cleanupCompleted = false;
                            failures ??= [];
                            failures.Add(exception);
                        }
                    }
                    if (!cleanupCompleted)
                    {
                        lock (m_historianBuildersLock)
                        {
                            if (!m_historianBuilders.Contains(builder))
                            {
                                m_historianBuilders.Add(builder);
                            }
                        }
                    }
                }
                try
                {
                    m_historianRegistry?.Dispose();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failures ??= [];
                    failures.Add(exception);
                }
                if (failures != null)
                {
                    throw new AggregateException(
                        "One or more candidate historian resources failed during cleanup.",
                        failures);
                }
                lock (m_historianBuildersLock)
                {
                    m_historianBuilders.Clear();
                    m_historianBuildersDisposed = true;
                }
                ReleaseProvisionalSources();
                ReleaseAbandonedReferences();
            }

            /// <inheritdoc/>
            public Action<byte> ClaimServiceLevelControl()
            {
                EnsurePublishedRegistryAccess();
                lock (m_serviceLevelControlLock)
                {
                    if (m_serviceLevelControlRevoked)
                    {
                        throw new InvalidOperationException(
                            "The NodeManager generation no longer owns service-level publication.");
                    }
                    if (m_serviceLevelControlWriter is not null)
                    {
                        throw new InvalidOperationException(
                            "This NodeManager generation already claimed service-level publication.");
                    }
                }
                if (m_server is not IServerServiceLevelControl control)
                {
                    throw new NotSupportedException("The server does not provide service-level control.");
                }
                Action<byte> publishLevel = control.ClaimServiceLevelControl();
                lock (m_serviceLevelControlLock)
                {
                    if (m_serviceLevelControlRevoked)
                    {
                        if (m_server is IServerServiceLevelControlRevocation revocation)
                        {
                            revocation.ReleaseServiceLevelControl(publishLevel);
                        }
                        throw new InvalidOperationException(
                            "The NodeManager generation no longer owns service-level publication.");
                    }
                    m_serviceLevelControlWriter = publishLevel;
                }
                return level =>
                {
                    EnsurePublishedRegistryAccess();
                    if (Volatile.Read(ref m_serviceLevelControlRevoked))
                    {
                        throw new InvalidOperationException(
                            "The NodeManager generation no longer owns service-level publication.");
                    }
                    publishLevel(level);
                };
            }

            /// <summary>
            /// Revokes this generation's service-level writer and releases the serving server's claim.
            /// </summary>
            internal void RevokeServiceLevelControl()
            {
                Action<byte>? writer;
                lock (m_serviceLevelControlLock)
                {
                    if (m_serviceLevelControlRevoked)
                    {
                        return;
                    }
                    Volatile.Write(ref m_serviceLevelControlRevoked, true);
                    writer = m_serviceLevelControlWriter;
                    m_serviceLevelControlWriter = null;
                }
                if (writer is not null && m_server is IServerServiceLevelControlRevocation revocation)
                {
                    revocation.ReleaseServiceLevelControl(writer);
                }
            }

            /// <inheritdoc/>
            public ServerSystemContext CreateSystemContext(ISession session)
            {
                return DefaultSystemContext.Copy(session);
            }

            /// <inheritdoc/>
            public T? FindPredefinedNode<T>(NodeId nodeId) where T : NodeState
                => m_server.FindPredefinedNode<T>(nodeId);

            /// <inheritdoc/>
            public IEnumerable<T> FindNodeManagers<T>() where T : class => m_server.FindNodeManagers<T>();

            /// <inheritdoc/>
            public void ReportEvent(IFilterTarget e) => m_server.ReportEvent(e);

            /// <inheritdoc/>
            public void ReportEvent(ISystemContext context, IFilterTarget e) => m_server.ReportEvent(context, e);

            /// <inheritdoc/>
            public ValueTask ReportEventAsync(IFilterTarget e, CancellationToken cancellationToken = default)
                => m_server.ReportEventAsync(e, cancellationToken);

            /// <inheritdoc/>
            public ValueTask ReportEventAsync(
                ISystemContext context, IFilterTarget e, CancellationToken cancellationToken = default)
                => m_server.ReportEventAsync(context, e, cancellationToken);

            /// <inheritdoc/>
            public void ReportAuditEvent(ISystemContext context, AuditEventState e)
                => m_server.ReportAuditEvent(context, e);

            /// <inheritdoc/>
            public void ConditionRefresh(OperationContext context, uint subscriptionId)
                => m_server.ConditionRefresh(context, subscriptionId);

            /// <inheritdoc/>
            public void ConditionRefresh2(OperationContext context, uint subscriptionId, uint monitoredItemId)
                => m_server.ConditionRefresh2(context, subscriptionId, monitoredItemId);

            /// <inheritdoc/>
            public void UpdateServerStatus(Action<ServerStatusValue> action) => m_server.UpdateServerStatus(action);

            /// <inheritdoc/>
            public void UpdateServerDiagnostics(Action<ServerDiagnosticsSummaryDataType> update)
                => m_server.UpdateServerDiagnostics(update);

            /// <inheritdoc/>
            public ValueTask CloseSessionAsync(
                OperationContext context, NodeId sessionId, bool deleteSubscriptions,
                CancellationToken cancellationToken = default)
                => m_server.CloseSessionAsync(context, sessionId, deleteSubscriptions, cancellationToken);

            /// <inheritdoc/>
            public ValueTask DeleteSubscriptionAsync(uint subscriptionId, CancellationToken cancellationToken = default)
                => m_server.DeleteSubscriptionAsync(subscriptionId, cancellationToken);

            /// <summary>
            /// Retains an owned node's external reference until the candidate is published.
            /// </summary>
            internal bool TryStageExternalReference(
                AsyncCustomNodeManager owner,
                NodeState target,
                LocalReference reference)
            {
                lock (m_externalReferencesLock)
                {
                    if (IsDetached)
                    {
                        throw new InvalidOperationException(
                            "External references cannot be added to a detached node manager.");
                    }
                    if (!IsStaging)
                    {
                        return false;
                    }
                    bool exists = m_stagedNodeReferences.Exists(entry =>
                        ReferenceEquals(entry.Target, target) && SameReference(entry.Reference, reference));
                    if (!exists)
                    {
                        m_stagedNodeReferences.Add((owner, target, reference));
                    }
                    if (m_preparedExternalReferences is { } references)
                    {
                        AddPreparedReference(references, reference, target);
                    }
                    return true;
                }
            }

            /// <summary>
            /// Checks whether a staged owned-node reference requires the source at publication.
            /// </summary>
            internal bool RequiresExternalSource(NodeId sourceId)
            {
                lock (m_externalReferencesLock)
                {
                    return m_stagedNodeReferences.Exists(entry => entry.Reference.SourceId == sourceId);
                }
            }

            /// <summary>
            /// Connects staged references to the dictionary used by address-space preparation.
            /// </summary>
            internal void TrackExternalReferences(Dictionary<NodeId, IList<IReference>> references)
            {
                lock (m_externalReferencesLock)
                {
                    if (m_preparedExternalReferences is not null)
                    {
                        throw new InvalidOperationException("External reference preparation has already started.");
                    }
                    m_preparedExternalReferences = references;
                    foreach (var entry in m_stagedNodeReferences)
                    {
                        if (ReferenceEquals(entry.Owner.Find(entry.Target.NodeId), entry.Target))
                        {
                            AddPreparedReference(references, entry.Reference, entry.Target);
                        }
                    }
                }
            }

            /// <summary>
            /// Discards private contributions targeting a node removed before publication.
            /// </summary>
            internal void DiscardNodeReferences(NodeState target)
            {
                lock (m_externalReferencesLock)
                {
                    m_stagedNodeReferences.RemoveAll(entry => ReferenceEquals(entry.Target, target));
                    if (m_preparedExternalReferences is not { } references)
                    {
                        return;
                    }
                    foreach (KeyValuePair<NodeId, IList<IReference>> source in references)
                    {
                        for (int index = source.Value.Count - 1; index >= 0; index--)
                        {
                            IReference reference = source.Value[index];
                            if (!reference.TargetId.IsAbsolute && (NodeId)reference.TargetId == target.NodeId &&
                                (reference is not OwnedExternalReference owned ||
                                    ReferenceEquals(owned.TargetNode, target)))
                            {
                                source.Value.RemoveAt(index);
                                RemoveCapturedReference(source.Key, reference);
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// Forgets published contributions removed before their manager retires.
            /// </summary>
            internal void DiscardCapturedReferences(List<LocalReference> references)
            {
                lock (m_externalReferencesLock)
                {
                    foreach (LocalReference reference in references)
                    {
                        bool belongsToRemovedTarget = true;
                        if (m_externalReferences.TryGetValue(
                            reference.SourceId, out ReferenceDictionary<NodeState?>? captured))
                        {
                            var key = new NodeStateReference(
                                reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
                            if (captured.TryGetValue(key, out NodeState? target))
                            {
                                belongsToRemovedTarget = reference.DeletedTarget is null ||
                                    target is null ||
                                    ReferenceEquals(reference.DeletedTarget, target);
                                if (belongsToRemovedTarget)
                                {
                                    captured.Remove(key);
                                }
                            }
                        }
                        if (belongsToRemovedTarget &&
                            reference.SourceId == ObjectIds.Server &&
                            reference.ReferenceTypeId == ReferenceTypeIds.HasNotifier &&
                            !reference.IsInverse &&
                            (reference.DeletedTarget is null ||
                                !m_pendingRootNotifierReferences.TryGetValue(
                                    reference.TargetId, out NodeState? notifier) ||
                                ReferenceEquals(reference.DeletedTarget, notifier)))
                        {
                            m_pendingRootNotifierReferences.Remove(reference.TargetId);
                        }
                    }
                }
            }

            /// <summary>
            /// Discards staged references whose exact target was replaced before publication.
            /// </summary>
            internal void PruneStagedExternalReferences()
            {
                lock (m_externalReferencesLock)
                {
                    for (int index = m_stagedNodeReferences.Count - 1; index >= 0; index--)
                    {
                        var entry = m_stagedNodeReferences[index];
                        if (ReferenceEquals(entry.Owner.Find(entry.Target.NodeId), entry.Target))
                        {
                            continue;
                        }
                        m_stagedNodeReferences.RemoveAt(index);
                        int currentIndex = m_stagedNodeReferences.FindIndex(candidate =>
                            SameReference(candidate.Reference, entry.Reference) &&
                            ReferenceEquals(candidate.Owner.Find(candidate.Target.NodeId), candidate.Target));
                        if (m_preparedExternalReferences is not { } references)
                        {
                            continue;
                        }
                        if (currentIndex < 0)
                        {
                            RemovePreparedReference(references, entry.Reference);
                        }
                        else
                        {
                            var current = m_stagedNodeReferences[currentIndex];
                            AddPreparedReference(references, current.Reference, current.Target);
                        }
                    }
                }
            }

            /// <summary>
            /// Captures the contributions whose removal belongs to publication, not address-space deletion.
            /// </summary>
            /// <param name="references">The candidate's external contributions.</param>
            internal void CaptureExternalReferences(Dictionary<NodeId, IList<IReference>> references)
            {
                lock (m_externalReferencesLock)
                {
                    foreach (KeyValuePair<NodeId, IList<IReference>> source in references)
                    {
                        var contributed = new ReferenceDictionary<NodeState?>();
                        foreach (IReference reference in source.Value)
                        {
                            contributed[new NodeStateReference(
                                reference.ReferenceTypeId, reference.IsInverse, reference.TargetId)] =
                                (reference as OwnedExternalReference)?.TargetNode;
                        }
                        m_externalReferences.Add(source.Key, contributed);
                    }
                    m_externalReferencesCaptured = true;
                }
            }

            /// <summary>
            /// Records a published contribution and its exact target.
            /// </summary>
            internal void CapturePublishedReference(LocalReference reference, NodeState target)
            {
                lock (m_externalReferencesLock)
                {
                    if (IsStaging || IsDetached || !m_externalReferencesCaptured)
                    {
                        throw new InvalidOperationException(
                            "The node manager cannot record a published reference.");
                    }
                    if (!m_externalReferences.TryGetValue(
                        reference.SourceId, out ReferenceDictionary<NodeState?>? captured))
                    {
                        m_externalReferences.Add(reference.SourceId, captured = new ReferenceDictionary<NodeState?>());
                    }
                    captured[new NodeStateReference(
                        reference.ReferenceTypeId, reference.IsInverse, reference.TargetId)] = target;
                }
            }

            /// <summary>
            /// Excludes transaction-owned contributions from legacy inverse-reference cleanup.
            /// </summary>
            /// <param name="references">The inverse references discovered during deletion.</param>
            internal void ExcludePreparedReferences(List<LocalReference> references)
            {
                lock (m_externalReferencesLock)
                {
                    references.RemoveAll(reference =>
                        m_externalReferences.TryGetValue(
                            reference.SourceId, out ReferenceDictionary<NodeState?>? contributed) &&
                        contributed.TryGetValue(
                            new NodeStateReference(
                                reference.ReferenceTypeId, reference.IsInverse, reference.TargetId),
                            out NodeState? target) &&
                        (reference.DeletedTarget is null ||
                            target is null ||
                            ReferenceEquals(reference.DeletedTarget, target)));
                }
            }

            /// <summary>
            /// Releases detached contribution metadata after reference cleanup completes.
            /// </summary>
            internal void ReleaseCapturedReferences()
            {
                lock (m_externalReferencesLock)
                {
                    if (!IsDetached)
                    {
                        throw new InvalidOperationException("The node manager must be detached before releasing references.");
                    }
                    m_externalReferences.Clear();
                    m_pendingRootNotifierReferences.Clear();
                    m_withdrawnRootNotifierReferences.Clear();
                    m_stagedNodeReferences.Clear();
                    m_preparedExternalReferences = null;
                }
            }

            /// <summary>
            /// Binds cached metadata handles to canonical storage after compatible publication.
            /// </summary>
            internal void Bind()
            {
                NamespaceUris.BindTo(m_servingNamespaceUris);
                ServerUris.BindTo(m_servingServerUris);
                TypeTree.BindTo(m_servingTypes);
                ((EncodeableFactory)Factory).BindTo(m_servingFactory);
                lock (m_externalReferencesLock)
                {
                    Volatile.Write(ref m_isStaging, false);
                    m_preparedExternalReferences = null;
                    m_stagedNodeReferences.Clear();
                    m_pendingRootNotifierReferences.Clear();
                }
            }

            /// <summary>
            /// Retains candidate namespace registrations without changing serving routing.
            /// </summary>
            internal void StageNamespaceRegistration(string namespaceUri)
            {
                m_pendingNamespaces.Add(namespaceUri);
            }

            /// <summary>
            /// Removes a candidate registration without changing serving routing.
            /// </summary>
            internal bool UnstageNamespaceRegistration(string namespaceUri)
            {
                return m_pendingNamespaces.Remove(namespaceUri);
            }

            /// <summary>
            /// Retains a root-notifier reference contribution until external-reference publication.
            /// </summary>
            internal bool TryStageRootNotifierReference(NodeState notifier)
            {
                lock (m_externalReferencesLock)
                {
                    if (IsDetached)
                    {
                        throw new ObjectDisposedException(nameof(StagingServer));
                    }
                    if (!IsStaging)
                    {
                        return false;
                    }
                    m_pendingRootNotifierReferences[notifier.NodeId] = notifier;
                    m_withdrawnRootNotifierReferences.Remove(notifier.NodeId);
                    if (m_preparedExternalReferences is { } references)
                    {
                        AddPreparedReference(
                            references,
                            new LocalReference(ObjectIds.Server, ReferenceTypeIds.HasNotifier, false, notifier.NodeId),
                            notifier);
                    }
                    return true;
                }
            }

            /// <summary>
            /// Removes a root-notifier reference contribution that was withdrawn during preparation.
            /// </summary>
            internal void UnstageRootNotifierReference(NodeState notifier)
            {
                lock (m_externalReferencesLock)
                {
                    if (!IsStaging)
                    {
                        return;
                    }
                    if (m_pendingRootNotifierReferences.TryGetValue(notifier.NodeId, out NodeState? current) &&
                        !ReferenceEquals(current, notifier))
                    {
                        return;
                    }
                    m_pendingRootNotifierReferences.Remove(notifier.NodeId);
                    m_withdrawnRootNotifierReferences.Add(notifier.NodeId);
                    if (m_preparedExternalReferences is { } references)
                    {
                        RemovePreparedReference(
                            references,
                            new LocalReference(ObjectIds.Server, ReferenceTypeIds.HasNotifier, false, notifier.NodeId));
                    }
                }
            }

            /// <summary>
            /// Gets roots withdrawn while prepared reference updates may already have captured them.
            /// </summary>
            internal NodeId[] GetWithdrawnRootNotifierReferences()
            {
                lock (m_externalReferencesLock)
                {
                    return [.. m_withdrawnRootNotifierReferences];
                }
            }

            /// <summary>
            /// Releases a withdrawn root after its serving reference has been reconciled.
            /// </summary>
            internal void ForgetWithdrawnRootNotifierReference(NodeId notifierId)
            {
                lock (m_externalReferencesLock)
                {
                    m_withdrawnRootNotifierReferences.Remove(notifierId);
                }
            }

            /// <summary>
            /// Merges retained root-notifier contributions into the prepared external-reference set.
            /// </summary>
            internal void AppendRootNotifierReferences(
                Dictionary<NodeId, IList<IReference>> externalReferences)
            {
                lock (m_externalReferencesLock)
                {
                    foreach (NodeState notifier in m_pendingRootNotifierReferences.Values)
                    {
                        AddPreparedReference(
                            externalReferences,
                            new LocalReference(ObjectIds.Server, ReferenceTypeIds.HasNotifier, false, notifier.NodeId),
                            notifier);
                    }
                }
            }

            /// <summary>
            /// Adds a contribution once and records additions made after the initial capture.
            /// </summary>
            private void AddPreparedReference(
                Dictionary<NodeId, IList<IReference>> references,
                LocalReference reference,
                NodeState? target = null)
            {
                if (!references.TryGetValue(reference.SourceId, out IList<IReference>? sourceReferences))
                {
                    references.Add(reference.SourceId, sourceReferences = []);
                }
                int existingIndex = -1;
                for (int index = 0; index < sourceReferences.Count; index++)
                {
                    if (SameReference(sourceReferences[index], reference))
                    {
                        existingIndex = index;
                        break;
                    }
                }
                NodeStateReference addition = target is null
                    ? new NodeStateReference(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId)
                    : new OwnedExternalReference(reference.ReferenceTypeId, reference.IsInverse, target);
                if (existingIndex < 0)
                {
                    sourceReferences.Add(addition);
                }
                else if (target is not null)
                {
                    sourceReferences[existingIndex] = addition;
                }
                if (m_externalReferencesCaptured)
                {
                    if (!m_externalReferences.TryGetValue(
                        reference.SourceId, out ReferenceDictionary<NodeState?>? captured))
                    {
                        m_externalReferences.Add(reference.SourceId, captured = new ReferenceDictionary<NodeState?>());
                    }
                    if (target is not null || !captured.ContainsKey(addition))
                    {
                        captured[addition] = target ??
                            (existingIndex >= 0
                                ? (sourceReferences[existingIndex] as OwnedExternalReference)?.TargetNode
                                : null);
                    }
                }
            }

            /// <summary>
            /// Removes one staged contribution from the prepared and captured sets.
            /// </summary>
            private void RemovePreparedReference(
                Dictionary<NodeId, IList<IReference>> references,
                LocalReference reference)
            {
                if (!references.TryGetValue(reference.SourceId, out IList<IReference>? sourceReferences))
                {
                    return;
                }
                for (int index = sourceReferences.Count - 1; index >= 0; index--)
                {
                    IReference existing = sourceReferences[index];
                    if (SameReference(existing, reference))
                    {
                        sourceReferences.RemoveAt(index);
                        RemoveCapturedReference(reference.SourceId, existing);
                    }
                }
            }

            /// <summary>
            /// Removes a contribution from the captured set when it has already been captured.
            /// </summary>
            private void RemoveCapturedReference(NodeId sourceId, IReference reference)
            {
                if (m_externalReferences.TryGetValue(sourceId, out ReferenceDictionary<NodeState?>? captured))
                {
                    captured.Remove(new NodeStateReference(
                        reference.ReferenceTypeId, reference.IsInverse, reference.TargetId));
                }
            }

            /// <summary>
            /// Releases an unpublished candidate's private reference metadata after disposal.
            /// </summary>
            private void ReleaseAbandonedReferences()
            {
                if (IsStaging && IsDetached)
                {
                    ReleaseCapturedReferences();
                }
            }

            /// <summary>
            /// Compares the external source and reference coordinates.
            /// </summary>
            private static bool SameReference(LocalReference left, LocalReference right)
            {
                return left.SourceId == right.SourceId &&
                    left.ReferenceTypeId == right.ReferenceTypeId &&
                    left.IsInverse == right.IsInverse &&
                    left.TargetId == right.TargetId;
            }

            /// <summary>
            /// Compares a prepared reference with a local contribution.
            /// </summary>
            private static bool SameReference(IReference left, LocalReference right)
            {
                return left.ReferenceTypeId == right.ReferenceTypeId &&
                    left.IsInverse == right.IsInverse &&
                    !left.TargetId.IsAbsolute &&
                    (NodeId)left.TargetId == right.TargetId;
            }

            /// <summary>
            /// Claims the candidate without publishing its namespace registrations.
            /// </summary>
            internal void Attach(IAsyncNodeManager manager)
            {
                if (s_borrowedCandidates.TryGetValue(manager, out StagingServer? borrowedServer))
                {
                    if (!ReferenceEquals(borrowedServer, this))
                    {
                        throw new NodeManagerAlreadyRegisteredException();
                    }
                    return;
                }
                if (!ReferenceEquals(s_candidates.GetValue(manager, _ => this), this))
                {
                    throw new NodeManagerAlreadyRegisteredException();
                }
            }

            /// <summary>
            /// Preserves this manager's view privately before cleanup.
            /// </summary>
            internal void Detach()
            {
                lock (m_externalReferencesLock)
                {
                    if (IsDetached)
                    {
                        return;
                    }
                    Volatile.Write(ref m_isDetached, true);
                    Volatile.Write(ref m_registeredFacade, null);
                }
                RetireHistorianRegistrations();
                RevokeServiceLevelControl();
                TypeTree.Detach();
                ((EncodeableFactory)Factory).Detach();
                NamespaceUris.Detach();
                ServerUris.Detach();
            }

            /// <summary>
            /// Keeps non-historian shared registries out of private preparation; runtime services bind during readiness.
            /// </summary>
            private void EnsurePublishedRegistryAccess()
            {
                if (IsStaging || IsDetached)
                {
                    throw new InvalidOperationException("Registry access requires a published generation.");
                }
            }

            /// <summary>The running server whose non-metadata services are forwarded.</summary>
            private readonly IServerInternal m_server;

            /// <summary>Looks up resources owned by the lifecycle.</summary>
            private readonly Func<IAsyncNodeManager, bool>? m_isRegistered;

            /// <summary>Looks up exact facades owned by the lifecycle.</summary>
            private readonly Func<IAsyncNodeManager, bool>? m_isExactlyRegistered;

            /// <summary>Protects the borrowed factory's declaration and completion.</summary>
            private readonly Lock m_borrowedDeclarationLock = new();

            /// <summary>The cleanup lease transferred by the factory.</summary>
            private BorrowedNodeManagerLeaseState? m_borrowedLease;

            /// <summary>The declared candidate facade.</summary>
            private IAsyncNodeManager? m_declaredBorrowedCandidate;

            /// <summary>The first declaration failure, even if the factory catches it.</summary>
            private ExceptionDispatchInfo? m_borrowedDeclarationFailure;

            /// <summary>The current or completed declaration.</summary>
            private TaskCompletionSource<bool>? m_borrowedDeclaration;

            /// <summary>Whether the borrowed factory has returned or failed.</summary>
            private bool m_borrowedFactoryClosed;

            /// <summary>The exact facade associated with this candidate.</summary>
            private IAsyncNodeManager? m_registeredFacade;

            /// <summary>The source identities created before the candidate was selected.</summary>
            private readonly List<(
                IAsyncNodeManager Identity,
                IAsyncNodeManager Resource,
                NodeManagerGeneration Generation)> m_provisionalSources = [];

            /// <summary>Protects provisional source ownership and factory completion.</summary>
            private readonly Lock m_provisionalSourcesLock = new();

            /// <summary>The resource selected by the completed factory, if any.</summary>
            private IAsyncNodeManager? m_sourceIdentity;

            /// <summary>The facade selected by the completed factory, if any.</summary>
            private IAsyncNodeManager? m_facadeIdentity;

            /// <summary>Whether the factory result has been selected or cleanup has started.</summary>
            private bool m_factoryCompleted;

            /// <summary>Whether the candidate's metadata is still private.</summary>
            private bool m_isStaging = true;

            /// <summary>Whether the candidate's metadata handles have been detached.</summary>
            private bool m_isDetached;

            /// <summary>Roots withdrawn before publication and reconciled against the serving view.</summary>
            private readonly HashSet<NodeId> m_withdrawnRootNotifierReferences = [];

            /// <summary>Canonical handles captured before any durable publication decision.</summary>
            private readonly NamespaceTable m_servingNamespaceUris;
            private readonly StringTable m_servingServerUris;
            private readonly TypeTable m_servingTypes;
            private readonly EncodeableFactory m_servingFactory;
            private readonly StagedHistorianProviderRegistry? m_historianRegistry;
            private readonly Lock m_historianBuildersLock = new();
            private readonly List<HistorianBuilder> m_historianBuilders = [];

            /// <summary>Retains namespace registration intent until routing publication.</summary>
            private readonly HashSet<string> m_pendingNamespaces = [];

            /// <summary>Retains root-notifier reference intent until external-reference publication.</summary>
            private readonly Dictionary<NodeId, NodeState> m_pendingRootNotifierReferences = [];

            /// <summary>Serializes private external-reference bookkeeping.</summary>
            private readonly Lock m_externalReferencesLock = new();

            /// <summary>Retains the exact owners of staged reference targets.</summary>
            private readonly List<(AsyncCustomNodeManager Owner, NodeState Target, LocalReference Reference)>
                m_stagedNodeReferences = [];

            /// <summary>The candidate's mutable external-reference set before publication.</summary>
            private Dictionary<NodeId, IList<IReference>>? m_preparedExternalReferences;

            /// <summary>Frozen reference contributions reconciled by publication before physical deletion.</summary>
            private readonly Dictionary<NodeId, ReferenceDictionary<NodeState?>> m_externalReferences = [];

            /// <summary>Whether the candidate's contributions have been captured for retirement.</summary>
            private bool m_externalReferencesCaptured;

            private readonly Lock m_serviceLevelControlLock = new();
            private Action<byte>? m_serviceLevelControlWriter;
            private bool m_serviceLevelControlRevoked;
            private bool m_historianBuildersDisposed;
        }
    }
}
