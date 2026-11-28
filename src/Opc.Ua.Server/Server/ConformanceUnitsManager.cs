// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Exposes the server's initialized conformance catalog without depending on its datastore implementation.
    /// </summary>
    public interface IConformanceUnitsManagerProvider
    {
        /// <summary>
        /// Gets the shared catalog, or null before initialization or while a generation is private or detached.
        /// The provider retains ownership of the catalog.
        /// </summary>
        ConformanceUnitsManager? ConformanceUnitsManager { get; }
    }

    /// <summary>
    /// An object that manages the conformance units and server profiles the
    /// server advertises on <c>Server/ServerCapabilities/ConformanceUnits</c>
    /// and <c>Server/ServerCapabilities/ServerProfileArray</c> (OPC UA Part 7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The published set is derived from visible routed node managers and other
    /// explicitly registered features implementing <see cref="IConformanceContributor"/>,
    /// rather than a fixed hard-coded list,
    /// so areas that are only enabled at runtime (for example Historical Access
    /// when history archiving is turned on) are advertised only when actually
    /// present. Registrations are aggregated and de-duplicated, then written to
    /// the address space through the <see cref="IDiagnosticsNodeManager"/>,
    /// mirroring how <see cref="ModellingRulesManager"/> delegates to it.
    /// </para>
    /// <para>
    /// The manager reads the current visible managers and explicitly registered
    /// contributors again on every <see cref="PublishAsync"/>, because the capabilities
    /// describe the current configuration of the server (OPC 10000-5 §6.3.2):
    /// a unit or profile a contributor no longer reports disappears with the next
    /// publish. The profiles the server declares in its configuration stay.
    /// </para>
    /// </remarks>
    public class ConformanceUnitsManager : IDisposable
    {
        /// <summary>
        /// Initializes the manager.
        /// </summary>
        public ConformanceUnitsManager(IServerInternal server)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_conformanceUnits = [];
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && !m_disposed)
            {
                m_disposed = true;
                m_publishLock.Dispose();
            }
        }

        /// <summary>
        /// Checks whether the conformance unit is registered as supported.
        /// </summary>
        /// <param name="conformanceUnit">
        /// The browse name of the conformance unit.
        /// </param>
        /// <returns>
        /// True if the conformance unit is supported as of the last
        /// registration or publish.
        /// </returns>
        public bool IsSupported(QualifiedName conformanceUnit)
        {
            if (conformanceUnit.IsNull)
            {
                return false;
            }

            lock (m_lock)
            {
                return m_conformanceUnits.Contains(conformanceUnit);
            }
        }

        /// <summary>
        /// Registers a contributor whose conformance units and server profiles
        /// the server advertises.
        /// </summary>
        /// <remarks>
        /// Registering a contributor again has no effect and does not read it
        /// again; every contributor is read again on each
        /// <see cref="PublishAsync"/>. An explicit registration is owned by
        /// the caller, even when the contributor is also a node manager: it
        /// is independent of the contribution the node manager lifecycle publishes for a committed manager.
        /// Retiring a published manager withdraws an overlapping explicit registration; an unpublished
        /// candidate's explicit registration remains owned by the caller until <see cref="Unregister"/>.
        /// </remarks>
        /// <param name="contributor">
        /// The contributor whose supported units and profiles are added.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="contributor"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">
        /// The contributor's NodeManager has already entered cleanup.
        /// </exception>
        public void Register(IConformanceContributor contributor)
        {
            if (contributor == null)
            {
                throw new ArgumentNullException(nameof(contributor));
            }

            lock (m_lock)
            {
                ContributorRegistration registration = m_contributorStates.GetValue(
                    contributor,
                    static current => new ContributorRegistration(current));
                if (registration.IsRegistered)
                {
                    return;
                }
                if (registration.CleanupRequested)
                {
                    throw new InvalidOperationException(
                        "A contributor whose NodeManager is being cleaned up cannot be registered again.");
                }
                registration.IsRegistered = true;
                m_contributors.Add(registration);
                m_registrationVersion++;
            }
            Aggregate();
        }

        /// <summary>
        /// Removes a contributor; its units and profiles disappear with the
        /// next <see cref="PublishAsync"/> unless another contributor reports
        /// them.
        /// </summary>
        /// <param name="contributor">The contributor to remove.</param>
        /// <returns><see langword="true"/> when the contributor was registered.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="contributor"/> is <c>null</c>.</exception>
        public bool Unregister(IConformanceContributor contributor)
        {
            if (contributor == null)
            {
                throw new ArgumentNullException(nameof(contributor));
            }

            bool removed;
            lock (m_lock)
            {
                removed = m_contributorStates.TryGetValue(
                    contributor, out ContributorRegistration? registration) &&
                    registration is { IsRegistered: true };
                if (removed)
                {
                    registration!.IsRegistered = false;
                    m_contributors.Remove(registration);
                    m_registrationVersion++;
                    registration.CompleteAdmissionClosedIfReady();
                    registration.CompleteDrainIfReady();
                }
            }
            if (removed)
            {
                Aggregate();
            }
            return removed;
        }

        /// <summary>
        /// Closes new registrations and captures the existing explicit reads of a NodeManager's contributors.
        /// </summary>
        /// <param name="nodeManager">The exact manager whose lifetime is ending.</param>
        /// <param name="withdraw">
        /// Whether to withdraw an overlapping explicit registration; private candidates leave it host-owned.
        /// </param>
        /// <returns>A task completed after registrations have ended and admitted reads have drained.</returns>
        internal Task BeginNodeManagerCleanup(IAsyncNodeManager nodeManager, bool withdraw)
        {
            var contributors = new HashSet<IConformanceContributor>(RefEqualityComparer.Default);
            CollectCleanupContributors(nodeManager, contributors);

            var drains = new List<Task>(contributors.Count);
            lock (m_lock)
            {
                foreach (IConformanceContributor contributor in contributors)
                {
                    ContributorRegistration registration = m_contributorStates.GetValue(
                        contributor,
                        static current => new ContributorRegistration(current));
                    registration.CleanupRequested = true;
                    if (withdraw && registration.IsRegistered)
                    {
                        registration.IsRegistered = false;
                        m_contributors.Remove(registration);
                        m_registrationVersion++;
                    }
                    registration.CompleteAdmissionClosedIfReady();
                    registration.CompleteDrainIfReady();
                    drains.Add(registration.Drain);
                }
            }
            return drains.Count switch
            {
                0 => Task.CompletedTask,
                1 => drains[0],
                _ => Task.WhenAll(drains)
            };
        }

        /// <summary>
        /// Captures the admission and drain state of a contributor whose cleanup has been requested.
        /// </summary>
        /// <param name="contributor">The exact contributor instance.</param>
        /// <param name="state">The current cleanup state, when one exists.</param>
        /// <returns>Whether cleanup has been requested for the contributor.</returns>
        internal bool TryGetCleanupDrainState(
            IConformanceContributor contributor,
            out ConformanceCleanupDrainState state)
        {
            lock (m_lock)
            {
                if (!m_contributorStates.TryGetValue(
                    contributor, out ContributorRegistration? registration) ||
                    registration is not { CleanupRequested: true })
                {
                    state = default;
                    return false;
                }
                state = new ConformanceCleanupDrainState(
                    registration.IsRegistered,
                    registration.ActiveReads,
                    registration.AdmissionClosed,
                    registration.Drain);
                return true;
            }
        }

        /// <summary>
        /// Reads every registered contributor again and publishes the
        /// aggregated conformance units and server profiles to the address
        /// space through the diagnostics node manager.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        public ValueTask PublishAsync(CancellationToken cancellationToken = default)
        {
            return PublishStagedAsync(null, cancellationToken);
        }

        /// <summary>
        /// Captures candidate contributions without making them visible in the active catalog.
        /// </summary>
        internal static Dictionary<NodeManagerGeneration, NodeManagerContribution> StageContributions(
            ArrayOf<PreparedNodeManager> candidates)
        {
            var staged = new Dictionary<NodeManagerGeneration, NodeManagerContribution>(RefEqualityComparer.Default);
            foreach (PreparedNodeManager candidate in candidates)
            {
                if (GetNodeManagerContributor(candidate.NodeManager) is { } contributor)
                {
                    staged.Add(candidate.Generation, new NodeManagerContribution(
                        [.. contributor.ConformanceUnits],
                        [.. contributor.ServerProfiles]));
                }
            }
            return staged;
        }

        /// <summary>
        /// Prepares the catalog read image without changing the current diagnostics values.
        /// </summary>
        internal ConformanceReadImage PrepareReadImage(
            DiagnosticsNodeManager diagnostics,
            NodeManagerRoutingTable.RoutingSnapshot routing,
            HashSet<NodeManagerGeneration> retiring,
            Dictionary<NodeManagerGeneration, NodeManagerContribution> staged)
        {
            var excluded = new HashSet<IConformanceContributor>(RefEqualityComparer.Default);
            foreach (NodeManagerGeneration generation in retiring)
            {
                CollectCleanupContributors(generation.NodeManager, excluded);
            }
            var contributions = ReadNodeManagerContributions(staged, routing, retiring);
            (ArrayOf<QualifiedName> units, ArrayOf<string> profiles) =
                AggregateExplicitContributors(contributions, excluded);
            return new ConformanceReadImage(diagnostics, units, profiles);
        }

        private static void CollectCleanupContributors(
            IAsyncNodeManager manager,
            HashSet<IConformanceContributor> contributors)
        {
            if (manager is IConformanceContributor contributor)
            {
                contributors.Add(contributor);
            }
            if (manager.SyncNodeManager is IConformanceContributor synchronous)
            {
                contributors.Add(synchronous);
            }
        }

        /// <summary>
        /// Publishes staged contributions only for generations visible in the serving routing image.
        /// </summary>
        internal async ValueTask PublishStagedAsync(
            Dictionary<NodeManagerGeneration, NodeManagerContribution>? staged,
            CancellationToken cancellationToken)
        {
            // Publishing is serialized, so the address space ends with the
            // set read last.
            await m_publishLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                IDiagnosticsNodeManager diagnostics = m_server.DiagnosticsNodeManager;
                ConformanceReadImage? image = (m_server.NodeManager as INodeManagerReadImageSource)
                    ?.GetReadImage(diagnostics) as ConformanceReadImage;
                (ArrayOf<QualifiedName> units, ArrayOf<string> profiles) = Aggregate(
                    refreshNodeManagers: true, staged: staged);
                if (diagnostics is DiagnosticsNodeManager builtIn)
                {
                    await builtIn.PublishConformanceUnitsAsync(units, profiles, image, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await diagnostics.PublishConformanceUnitsAsync(units, profiles, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                m_publishLock.Release();
            }
        }

        /// <summary>
        /// Reads the contributors and replaces the aggregated sets.
        /// </summary>
        /// <remarks>
        /// The contributors are read outside the lock: computing what they
        /// support may take locks of their own. A registration that changes
        /// while they are read makes the read stale, so it is repeated with
        /// the current contributors rather than replacing the set that
        /// registration already committed.
        /// </remarks>
        private (ArrayOf<QualifiedName> Units, ArrayOf<string> Profiles) Aggregate(
            bool refreshNodeManagers = false,
            Dictionary<NodeManagerGeneration, NodeManagerContribution>? staged = null)
        {
            if (refreshNodeManagers)
            {
                (ArrayOf<QualifiedName> units, ArrayOf<string> profiles) = ReadNodeManagerContributions(staged);
                lock (m_lock)
                {
                    m_nodeManagerUnits = units;
                    m_nodeManagerProfiles = profiles;
                    m_registrationVersion++;
                }
            }
            return AggregateExplicitContributors();
        }

        /// <summary>
        /// Reads committed managers while their exact generations remain admitted.
        /// </summary>
        /// <exception cref="InvalidOperationException">A contributor retired before admission.</exception>
        private (ArrayOf<QualifiedName> Units, ArrayOf<string> Profiles) ReadNodeManagerContributions(
            Dictionary<NodeManagerGeneration, NodeManagerContribution>? staged,
            NodeManagerRoutingTable.RoutingSnapshot? preparingFrom = null,
            HashSet<NodeManagerGeneration>? retiring = null)
        {
            var owners = new List<(IAsyncNodeManager Manager, NodeManagerGeneration Generation)>();
            if (m_server.NodeManager is IDynamicNodeManagerBatchHost host)
            {
                NodeManagerRoutingTable.RoutingSnapshot snapshot = preparingFrom ?? host.RoutingRevision;
                int visibleIndex = 0;
                for (int ii = 0; ii < snapshot.NodeManagers.Length; ii++)
                {
                    if (visibleIndex < snapshot.VisibleNodeManagers.Length &&
                        ReferenceEquals(snapshot.NodeManagers[ii], snapshot.VisibleNodeManagers[visibleIndex]))
                    {
                        if (retiring?.Contains(snapshot.Generations[ii]) != true)
                        {
                            owners.Add((snapshot.NodeManagers[ii], snapshot.Generations[ii]));
                        }
                        visibleIndex++;
                    }
                }
            }
            else if (m_server.NodeManager is { } nodeManager)
            {
                foreach (IAsyncNodeManager manager in nodeManager.AsyncNodeManagers)
                {
                    owners.Add((manager, NodeManagerGenerationRegistry.GetOrCreate(manager)));
                }
            }

            var admissions = new List<(NodeManagerGeneration Generation, int Stripe)>();
            try
            {
                foreach ((IAsyncNodeManager _, NodeManagerGeneration generation) in owners)
                {
                    if (!generation.Gate.TryEnter(out int stripe))
                    {
                        const string retired =
                            "A conformance contributor retired before its generation could be admitted.";
                        // A batch's view was superseded by a concurrent commit: the batch rebases and reads again.
                        throw preparingFrom is not null
                            ? new NodeManagerRoutingConflictException(retired, preparingFrom) { StaleDiscovery = true }
                            : new InvalidOperationException(retired);
                    }
                    admissions.Add((generation, stripe));
                }

                var units = new List<QualifiedName>();
                var profiles = new List<string>();
                var contributors = new List<IConformanceContributor>();
                foreach ((IAsyncNodeManager manager, NodeManagerGeneration generation) in owners)
                {
                    if (GetNodeManagerContributor(manager) is { } contributor &&
                        !contributors.Exists(current => ReferenceEquals(current, contributor)))
                    {
                        contributors.Add(contributor);
                        NodeManagerContribution contribution =
                            staged is not null && staged.TryGetValue(generation, out NodeManagerContribution? captured)
                                ? captured
                                : new NodeManagerContribution(contributor.ConformanceUnits, contributor.ServerProfiles);
                        foreach (QualifiedName unit in contribution.Units)
                        {
                            units.Add(unit);
                        }
                        foreach (string profile in contribution.Profiles)
                        {
                            profiles.Add(profile);
                        }
                    }
                }
                if (preparingFrom is not null && staged is not null)
                {
                    foreach (NodeManagerContribution candidate in staged.Values)
                    {
                        foreach (QualifiedName unit in candidate.Units)
                        {
                            units.Add(unit);
                        }
                        foreach (string profile in candidate.Profiles)
                        {
                            profiles.Add(profile);
                        }
                    }
                }
                return (units.ToArrayOf(), profiles.ToArrayOf());
            }
            finally
            {
                foreach ((NodeManagerGeneration generation, int stripe) in admissions)
                {
                    generation.Gate.Exit(stripe);
                }
            }
        }

        /// <summary>
        /// The capabilities captured for one exact candidate generation.
        /// </summary>
        internal sealed record NodeManagerContribution(ArrayOf<QualifiedName> Units, ArrayOf<string> Profiles);

        /// <summary>
        /// Holds the conformance values that become visible with one routing publication.
        /// </summary>
        internal sealed record ConformanceReadImage(
            IAsyncNodeManager Owner,
            ArrayOf<QualifiedName> Units,
            ArrayOf<string> Profiles) : INodeManagerReadImage;

        /// <summary>
        /// Finds the routed manager's contribution, including a resource behind a synchronous adapter.
        /// </summary>
        private static IConformanceContributor? GetNodeManagerContributor(IAsyncNodeManager manager)
        {
            if (manager is IConformanceContributor contributor)
            {
                return contributor;
            }
            // TODO: Drop the synchronous contribution lookup when legacy managers are removed.
#pragma warning disable CS0618, UA0031 // Keep contributions from wrapped synchronous managers.
            INodeManager? synchronous = manager.SyncNodeManager;
#pragma warning restore CS0618, UA0031
            if (synchronous is SyncNodeManagerAdapter adapter)
            {
                return adapter as IConformanceContributor ??
                    adapter.AsyncNodeManager as IConformanceContributor;
            }
            return synchronous as IConformanceContributor;
        }

        /// <summary>
        /// Combines explicit contributions with the last admitted manager snapshot.
        /// </summary>
        private (ArrayOf<QualifiedName> Units, ArrayOf<string> Profiles) AggregateExplicitContributors(
            (ArrayOf<QualifiedName> Units, ArrayOf<string> Profiles)? proposed = null,
            HashSet<IConformanceContributor>? excluded = null)
        {
            while (true)
            {
                ContributorRegistration[] contributors;
                ArrayOf<QualifiedName> nodeManagerUnits;
                ArrayOf<string> nodeManagerProfiles;
                long registrationVersion;
                lock (m_lock)
                {
                    var selected = new List<ContributorRegistration>(m_contributors.Count);
                    foreach (ContributorRegistration registration in m_contributors)
                    {
                        if (excluded?.Contains(registration.Contributor) != true)
                        {
                            selected.Add(registration);
                        }
                    }
                    contributors = [.. selected];
                    foreach (ContributorRegistration registration in contributors)
                    {
                        registration.ActiveReads++;
                    }
                    nodeManagerUnits = proposed?.Units ?? m_nodeManagerUnits;
                    nodeManagerProfiles = proposed?.Profiles ?? m_nodeManagerProfiles;
                    registrationVersion = m_registrationVersion;
                }

                try
                {
                    var units = new HashSet<QualifiedName>();
                    var profiles = new HashSet<string>(StringComparer.Ordinal);
                    var orderedProfiles = new List<string>();
                    foreach (QualifiedName unit in nodeManagerUnits)
                    {
                        if (!unit.IsNull)
                        {
                            units.Add(unit);
                        }
                    }
                    foreach (string profile in nodeManagerProfiles)
                    {
                        if (!string.IsNullOrEmpty(profile) && profiles.Add(profile))
                        {
                            orderedProfiles.Add(profile);
                        }
                    }
                    foreach (ContributorRegistration registration in contributors)
                    {
                        foreach (QualifiedName unit in registration.Contributor.ConformanceUnits)
                        {
                            if (!unit.IsNull)
                            {
                                units.Add(unit);
                            }
                        }
                        foreach (string profile in registration.Contributor.ServerProfiles)
                        {
                            if (!string.IsNullOrEmpty(profile) && profiles.Add(profile))
                            {
                                orderedProfiles.Add(profile);
                            }
                        }
                    }

                    lock (m_lock)
                    {
                        if (registrationVersion != m_registrationVersion)
                        {
                            continue;
                        }
                        if (proposed is null)
                        {
                            m_conformanceUnits = units;
                        }
                    }

                    var orderedUnits = new List<QualifiedName>(units);
                    orderedUnits.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
                    return (orderedUnits.ToArrayOf(), orderedProfiles.ToArrayOf());
                }
                finally
                {
                    lock (m_lock)
                    {
                        foreach (ContributorRegistration registration in contributors)
                        {
                            registration.ActiveReads--;
                            registration.CompleteDrainIfReady();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Tracks one exact contributor's explicit registration and admitted reads.
        /// </summary>
        private sealed class ContributorRegistration
        {
            /// <summary>
            /// Creates a registration state for one contributor instance.
            /// </summary>
            internal ContributorRegistration(IConformanceContributor contributor)
            {
                Contributor = contributor;
            }

            /// <summary>
            /// Gets the exact contributor.
            /// </summary>
            internal IConformanceContributor Contributor { get; }

            /// <summary>
            /// Gets or sets whether the host's explicit registration remains present.
            /// </summary>
            internal bool IsRegistered { get; set; }

            /// <summary>
            /// Gets or sets whether NodeManager cleanup has reserved this contributor.
            /// </summary>
            internal bool CleanupRequested { get; set; }

            /// <summary>
            /// Gets or sets the number of admitted explicit contributor reads.
            /// </summary>
            internal int ActiveReads { get; set; }

            /// <summary>
            /// Gets the task signaled when new explicit reads can no longer be admitted.
            /// </summary>
            internal Task AdmissionClosed => (m_admissionClosed ??= NewCompletion()).Task;

            /// <summary>
            /// Gets the task signaled when all admitted reads have completed.
            /// </summary>
            internal Task Drain => (m_drained ??= NewCompletion()).Task;

            /// <summary>
            /// Signals that cleanup has closed explicit read admission.
            /// </summary>
            internal void CompleteAdmissionClosedIfReady()
            {
                if (CleanupRequested && !IsRegistered)
                {
                    (m_admissionClosed ??= NewCompletion()).TrySetResult(true);
                }
            }

            /// <summary>
            /// Signals completion after registration and all previously admitted reads have ended.
            /// </summary>
            internal void CompleteDrainIfReady()
            {
                if (CleanupRequested && !IsRegistered && ActiveReads == 0)
                {
                    (m_drained ??= NewCompletion()).TrySetResult(true);
                }
            }

            /// <summary>
            /// Creates an asynchronous state-transition signal.
            /// </summary>
            private static TaskCompletionSource<bool> NewCompletion()
            {
                return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            private TaskCompletionSource<bool>? m_admissionClosed;
            private TaskCompletionSource<bool>? m_drained;
        }

        /// <summary>
        /// Describes a contributor's cleanup admission and pending read drain.
        /// </summary>
        internal readonly record struct ConformanceCleanupDrainState(
            bool IsRegistered,
            int ActiveReads,
            Task AdmissionClosed,
            Task Drain);

        private readonly Lock m_lock = new();
        private readonly SemaphoreSlim m_publishLock = new(1, 1);
        private readonly IServerInternal m_server;
        private readonly List<ContributorRegistration> m_contributors = [];

        private readonly ConditionalWeakTable<IConformanceContributor, ContributorRegistration> m_contributorStates =
            new();

        private ArrayOf<QualifiedName> m_nodeManagerUnits;
        private ArrayOf<string> m_nodeManagerProfiles;
        private HashSet<QualifiedName> m_conformanceUnits;
        private long m_registrationVersion;
        private bool m_disposed;
    }
}
