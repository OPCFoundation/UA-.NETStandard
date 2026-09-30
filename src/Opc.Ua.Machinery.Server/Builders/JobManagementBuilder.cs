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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.Machinery.Server.Jobs;
using Opc.Ua.Machinery.Jobs;
using MachineryJobsBrowseNames = Opc.Ua.Machinery.Jobs.BrowseNames;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Machinery.Server.Builders
{
    /// <summary>
    /// Assembles the OPC 40001-3 <c>JobManagement</c> object of a machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>JobManagementType</c> has exactly two mandatory children:
    /// <c>JobOrderControl</c>, an ISA-95 <c>JobOrderReceiverObjectType</c>, and
    /// <c>JobOrderResults</c>, an ISA-95 <c>JobResponseProviderObjectType</c>.
    /// The eleven job verbs — Store, StoreAndStart, Start, Stop, Abort, Pause,
    /// Resume, Clear, Cancel, Update, RevokeStart — belong to the ISA-95 type,
    /// not to OPC 40001-3, and are bound by the shared
    /// <see cref="Isa95JobControlV2Binder"/>.
    /// </para>
    /// <para>
    /// OPC 40001-3 needs no Device Integration of its own: it composes UA core
    /// and ISA-95 Job Control V2 only.
    /// </para>
    /// </remarks>
    public interface IJobManagementBuilder
    {
        /// <summary>
        /// Gets the job-management state being configured.
        /// </summary>
        JobManagementState State { get; }

        /// <summary>
        /// Binds the job verbs to an explicit receiver. When no receiver is
        /// bound, the builder resolves <see cref="IIsa95JobOrderReceiverV2"/>
        /// from the application services during
        /// <see cref="IMachineBuilder{TState}.BuildAsync"/>.
        /// </summary>
        /// <param name="receiver">The receiver to bind.</param>
        IJobManagementBuilder WithJobOrderReceiver(IIsa95JobOrderReceiverV2 receiver);

        /// <summary>
        /// Binds the two result-request methods to an explicit response
        /// provider. When none is bound, the builder resolves
        /// <see cref="IIsa95JobResponseProviderV2"/> from the application
        /// services.
        /// </summary>
        /// <param name="provider">The response provider to bind.</param>
        IJobManagementBuilder WithJobResponseProvider(IIsa95JobResponseProviderV2 provider);

        /// <summary>
        /// Advertises the predefined OPC 40001-3 job parameters and enforces
        /// their declared types.
        /// </summary>
        /// <remarks>
        /// <para>
        /// OPC 40001-3 §9 gives each predefined parameter a conformance unit of
        /// its own. The parameters travel inside the ISA-95 payload rather than
        /// as nodes, so a server can only claim them by recognising the IDs and
        /// refusing one that arrives carrying the wrong type — which is what
        /// this turns on.
        /// </para>
        /// <para>
        /// Parameters the series does not predefine keep travelling untouched.
        /// </para>
        /// </remarks>
        IJobManagementBuilder WithPredefinedParameters();

        /// <summary>
        /// Publishes the downloadable job-order list from
        /// <paramref name="catalog"/>.
        /// </summary>
        /// <param name="catalog">The job-order catalog.</param>
        IJobManagementBuilder WithJobOrderCatalog(IIsa95JobOrderCatalog catalog);
    }

    /// <summary>
    /// Implements <see cref="IJobManagementBuilder"/>: stages the OPC 40001-3
    /// <c>JobManagement</c> add-in on a machine and, once the machine is
    /// registered, binds the ISA-95 Job Control V2 verbs and keeps
    /// <c>JobOrderList</c> and <c>JobOrderResponseList</c> in step with the
    /// machine's own provider.
    /// </summary>
    internal sealed class JobManagementBuilder : IJobManagementBuilder, IAsyncDisposable
    {
        public static JobManagementBuilder Create(MachineryBuildScope scope, NodeState machine)
        {
            ushort namespaceIndex = MachineryBuilderUtilities.NamespaceIndex(
                scope.Context,
                Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs);
            JobManagementState jobManagement = MachineryBuilderUtilities.AddAddIn(
                scope.Context,
                machine,
                new QualifiedName(MachineryJobsBrowseNames.JobManagement, namespaceIndex),
                static (ctx, parent, name) =>
                    ctx.CreateInstanceOfJobManagementType(parent, name));
            scope.RecordFacet(MachineryFacet.JobManagement);
            return new JobManagementBuilder(scope, jobManagement);
        }

        private JobManagementBuilder(MachineryBuildScope scope, JobManagementState state)
        {
            m_scope = scope;
            State = state;

            m_binder = new Isa95JobControlV2Binder(
                scope.Context,
                scope.Context.NamespaceUris,
                RefreshListsAsync);

            V2.ISA95JobOrderReceiverObjectState control = State.JobOrderControl ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The generated JobManagementType instance is missing JobOrderControl.");
            m_binder.AddReceiverMethods(control);

            // JobOrderResponseList is an optional child of the generated
            // response provider type, and nodes can only be staged while the
            // machine is being built - BindAsync runs after registration, too
            // late for the node manager to index a new node. Create it here and
            // fill it there.
            if (State.JobOrderResults is { JobOrderResponseList: null } results)
            {
                results.AddJobOrderResponseList(scope.Context);
            }

            // The build only stages nodes; the actual binding runs once the
            // machine is registered, so a provider resolved from the hosting
            // container is looked up at the same point as every other service.
            scope.PostRegistrationActions.Add(BindAsync);
        }

        public JobManagementState State { get; }

        public IJobManagementBuilder WithJobOrderReceiver(IIsa95JobOrderReceiverV2 receiver)
        {
            m_scope.EnsureMutable();
            m_receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
            return this;
        }

        public IJobManagementBuilder WithJobResponseProvider(IIsa95JobResponseProviderV2 provider)
        {
            m_scope.EnsureMutable();
            m_responseProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            return this;
        }

        public IJobManagementBuilder WithJobOrderCatalog(IIsa95JobOrderCatalog catalog)
        {
            m_scope.EnsureMutable();
            m_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            return this;
        }

        public IJobManagementBuilder WithPredefinedParameters()
        {
            m_scope.EnsureMutable();
            m_predefinedParameters = true;
            return this;
        }

        private async ValueTask BindAsync(CancellationToken cancellationToken)
        {
            // Every list this builder publishes - JobOrderList and
            // JobOrderResponseList alike - is a variable on this machine's own
            // JobManagement block, so it has to be fed by this machine's own
            // provider. A machine that was given one through
            // WithJobOrderReceiver/WithJobOrderCatalog/WithJobResponseProvider
            // must therefore never fall back to the container: that singleton
            // belongs to another machine, and reading it here would publish
            // another machine's orders and responses - every machine in the
            // server would show one shared pair of lists.
            //
            // Decided before anything is defaulted, because the fallbacks below
            // would otherwise make every machine look like it had its own.
            bool ownProvider = m_receiver != null
                || m_catalog != null
                || m_responseProvider != null;

            IIsa95JobOrderReceiverV2? receiver = m_receiver ??
                m_scope.BuildContext.GetService<IIsa95JobOrderReceiverV2>();
            if (receiver == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "OPC 40001-3 JobManagement needs an ISA-95 Job Control V2 job-order " +
                    "receiver. Register one with AddInMemoryIsa95JobControlProvider() or " +
                    "supply it through WithJobOrderReceiver().");
            }

            // Resolved before InitializeOrderVariables, so MaxDownloadableJobOrders
            // reports the catalog's own limit rather than zero whenever the
            // catalog was not named explicitly.
            m_catalog ??= ownProvider
                ? FirstOf<IIsa95JobOrderCatalog>(m_receiver, m_responseProvider)
                : m_scope.BuildContext.GetService<IIsa95JobOrderCatalog>();

            if (m_predefinedParameters)
            {
                receiver = new PredefinedParameterJobOrderReceiver(receiver);
                m_scope.RecordFacet(MachineryFacet.JobPredefinedParameters);
            }

            V2.ISA95JobOrderReceiverObjectState control = State.JobOrderControl!;
            m_binder.BindOrderReceiver(control, receiver);
            m_binder.InitializeOrderVariables(
                control,
                m_catalog?.MaxDownloadableJobOrders ?? 0);
            control.JobOrderList!.OnSimpleReadValue = ReadJobOrderList;

            IIsa95JobResponseProviderV2? responseProvider = m_responseProvider ?? (ownProvider
                ? FirstOf<IIsa95JobResponseProviderV2>(m_receiver, m_catalog)
                : m_scope.BuildContext.GetService<IIsa95JobResponseProviderV2>());
            if (responseProvider != null && State.JobOrderResults != null)
            {
                State.JobOrderResults.EventNotifier = EventNotifiers.SubscribeToEvents;
                m_binder.BindResponseProvider(State.JobOrderResults, responseProvider);
                m_scope.RecordFacet(MachineryFacet.JobResults);

                // The two request methods answer one id or one state; neither
                // can express "every response", so the list variable stays empty
                // unless a catalog fills it.
                m_responseCatalog = ownProvider
                    ? FirstOf<IIsa95JobResponseCatalog>(m_receiver, m_catalog, responseProvider)
                    : m_scope.BuildContext.GetService<IIsa95JobResponseCatalog>();

                if (State.JobOrderResults.JobOrderResponseList is { } responseList)
                {
                    responseList.Value = [];
                    responseList.OnSimpleReadValue = ReadJobOrderResponseList;
                }
            }

            await RefreshListsAsync(cancellationToken).ConfigureAwait(false);

            // Isa95JobControlV2Binder only calls RefreshJobOrderListAsync back for
            // operations that went through its own bound method handlers - a
            // client calling Store/Start/... on JobOrderControl. Anything that
            // changes the catalog by another route (a PLC/MES integration or a
            // simulation calling IIsa95JobOrderReceiverV2/IIsa95JobExecutionController
            // directly, bypassing the OPC UA method call entirely) leaves the
            // published JobOrderList stale forever, because nothing else ever
            // triggers a refresh. Opc.Ua.ISA95.Server's stand-alone Isa95NodeManager
            // already closes this gap for a bare ISA-95 server by subscribing to
            // both change streams the provider can publish; OPC 40001-3's
            // JobManagement composes the same ISA-95 model and needs the same fix.
            // Resolved the same way as the catalog above, and for the same
            // reason: a machine with its own provider listens to that provider.
            IIsa95JobOrderCatalogChangeSource? catalogChanges = ownProvider
                ? FirstOf<IIsa95JobOrderCatalogChangeSource>(m_receiver, m_catalog, m_responseProvider)
                : m_scope.BuildContext.GetService<IIsa95JobOrderCatalogChangeSource>();
            IIsa95JobStatusSourceV2? statusSource = ownProvider
                ? FirstOf<IIsa95JobStatusSourceV2>(m_receiver, m_catalog, m_responseProvider)
                : m_scope.BuildContext.GetService<IIsa95JobStatusSourceV2>();

            // A response the machine reports while the order keeps running -
            // runs completed, quantity produced - changes neither the order's
            // state nor the order catalog, so without this stream the
            // published JobOrderResponseList would miss it until the order next
            // changes state. Only meaningful when a response catalog feeds the
            // list.
            IIsa95JobResponseChangeSource? responseChanges = m_responseCatalog == null
                ? null
                : ownProvider
                    ? FirstOf<IIsa95JobResponseChangeSource>(
                        m_receiver,
                        m_catalog,
                        m_responseProvider,
                        m_responseCatalog)
                    : m_scope.BuildContext.GetService<IIsa95JobResponseChangeSource>();
            if (catalogChanges != null || statusSource != null || responseChanges != null)
            {
                m_logger = m_scope.BuildContext.Manager.Server.Telemetry
                    .CreateLogger<JobManagementBuilder>();

                // Only created when a pump runs, so a machine without change
                // streams leaves nothing to dispose.
                m_changeStreamCts = new CancellationTokenSource();
                m_changeStreamTask = ObserveExternalChangesAsync(
                    catalogChanges, statusSource, responseChanges, m_changeStreamCts.Token);

                // Released on build rollback, or - once the machine is
                // registered - when its node manager is disposed.
                m_scope.RegisteredResources.Add(this);
            }
        }

        /// <summary>
        /// Returns the first bound object that implements <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The ISA-95 provider facet to look for.</typeparam>
        /// <param name="candidates">The explicitly bound objects, in priority order.</param>
        /// <remarks>
        /// A provider typically implements several ISA-95 facets on one object —
        /// <c>InMemoryIsa95JobControlProvider</c> implements all of them — so a
        /// caller binding it as the receiver has also supplied the catalog and
        /// both change streams without naming them.
        /// </remarks>
        private static T? FirstOf<T>(params object?[] candidates) where T : class
        {
            foreach (object? candidate in candidates)
            {
                if (candidate is T match)
                {
                    return match;
                }
            }
            return null;
        }

        /// <summary>
        /// Runs whichever of the three external change streams the resolved
        /// provider actually publishes: catalog changes and status changes
        /// refresh both lists, received responses refresh the
        /// JobOrderResponseList. A provider need not implement all of them —
        /// the in-memory demo provider does, but a real PLC/MES integration may
        /// only ever raise one kind of change.
        /// </summary>
        private async Task ObserveExternalChangesAsync(
            IIsa95JobOrderCatalogChangeSource? catalogChanges,
            IIsa95JobStatusSourceV2? statusSource,
            IIsa95JobResponseChangeSource? responseChanges,
            CancellationToken cancellationToken)
        {
            List<Task> pumps = [];
            if (catalogChanges != null)
            {
                pumps.Add(PumpCatalogChangesAsync(catalogChanges, cancellationToken));
            }
            if (statusSource != null)
            {
                pumps.Add(PumpJobStatusAsync(statusSource, cancellationToken));
            }
            if (responseChanges != null)
            {
                pumps.Add(PumpResponseChangesAsync(responseChanges, cancellationToken));
            }
            await Task.WhenAll(pumps).ConfigureAwait(false);
        }

        private async Task PumpResponseChangesAsync(
            IIsa95JobResponseChangeSource source, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (Isa95JobResponseChange _ in source
                    .SubscribeResponseChangesAsync(cancellationToken).ConfigureAwait(false))
                {
                    await RefreshJobOrderResponseListAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal teardown via DisposeAsync.
            }
            catch (Exception ex)
            {
                m_logger?.JobOrderChangeStreamFailed(ex);
            }
        }

        private async Task PumpCatalogChangesAsync(
            IIsa95JobOrderCatalogChangeSource source, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (Isa95JobOrderCatalogChange _ in source
                    .SubscribeCatalogChangesAsync(cancellationToken).ConfigureAwait(false))
                {
                    await RefreshListsAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal teardown via DisposeAsync.
            }
            catch (Exception ex)
            {
                m_logger?.JobOrderChangeStreamFailed(ex);
            }
        }

        private async Task PumpJobStatusAsync(
            IIsa95JobStatusSourceV2 source, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (Isa95JobStatusNotificationV2 _ in source
                    .SubscribeAsync(cancellationToken).ConfigureAwait(false))
                {
                    await RefreshListsAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal teardown via DisposeAsync.
            }
            catch (Exception ex)
            {
                m_logger?.JobOrderChangeStreamFailed(ex);
            }
        }

        /// <summary>
        /// Stops the external-change pumps. Reached on build rollback, or once
        /// the machine is registered when its node manager is disposed — see
        /// the comment where this is added to <c>RegisteredResources</c>.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (m_changeStreamCts == null)
            {
                return;
            }
            try
            {
                m_changeStreamCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed by a previous call.
            }
            try
            {
                await m_changeStreamTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected from the cancellation above.
            }
            m_changeStreamCts.Dispose();
        }

        /// <summary>
        /// Refreshes both published lists. A job order change and a job response
        /// arriving are the same event as far as a client is concerned — a job
        /// that ends produces both — so the two lists are always refreshed
        /// together rather than drifting apart between events.
        /// </summary>
        private async ValueTask RefreshListsAsync(CancellationToken cancellationToken)
        {
            await RefreshJobOrderListAsync(cancellationToken).ConfigureAwait(false);
            await RefreshJobOrderResponseListAsync(cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask RefreshJobOrderListAsync(CancellationToken cancellationToken)
        {
            IIsa95JobOrderCatalog? catalog = m_catalog;
            if (catalog == null)
            {
                return;
            }
            long generation = Interlocked.Increment(ref m_refreshGeneration);
            ArrayOf<V2.ISA95JobOrderAndStateDataType> orders = m_binder.NormalizeJobOrders(
                await catalog.GetJobOrdersV2Async(cancellationToken).ConfigureAwait(false));
            lock (m_refreshLock)
            {
                if (generation <= m_appliedGeneration)
                {
                    return;
                }
                m_jobOrders = orders;
                m_appliedGeneration = generation;
            }
        }

        private ServiceResult ReadJobOrderList(
            ISystemContext context,
            NodeState node,
            ref Variant value)
        {
            ArrayOf<V2.ISA95JobOrderAndStateDataType> snapshot;
            lock (m_refreshLock)
            {
                snapshot = m_jobOrders;
            }
            value = Variant.FromStructure(snapshot);
            return ServiceResult.Good;
        }

        private async ValueTask RefreshJobOrderResponseListAsync(CancellationToken cancellationToken)
        {
            IIsa95JobResponseCatalog? catalog = m_responseCatalog;
            if (catalog == null)
            {
                return;
            }
            long generation = Interlocked.Increment(ref m_responseRefreshGeneration);
            ArrayOf<V2.ISA95JobResponseDataType> responses = m_binder.NormalizeResponses(
                await catalog.GetJobResponsesV2Async(cancellationToken).ConfigureAwait(false));
            lock (m_refreshLock)
            {
                if (generation <= m_appliedResponseGeneration)
                {
                    return;
                }
                m_jobResponses = responses;
                m_appliedResponseGeneration = generation;
            }
        }

        private ServiceResult ReadJobOrderResponseList(
            ISystemContext context,
            NodeState node,
            ref Variant value)
        {
            ArrayOf<V2.ISA95JobResponseDataType> snapshot;
            lock (m_refreshLock)
            {
                snapshot = m_jobResponses;
            }
            value = Variant.FromStructure(snapshot);
            return ServiceResult.Good;
        }

        private readonly MachineryBuildScope m_scope;
        private readonly Isa95JobControlV2Binder m_binder;
        private readonly Lock m_refreshLock = new();
        private CancellationTokenSource? m_changeStreamCts;
        private IIsa95JobOrderReceiverV2? m_receiver;
        private IIsa95JobResponseProviderV2? m_responseProvider;
        private IIsa95JobOrderCatalog? m_catalog;
        private IIsa95JobResponseCatalog? m_responseCatalog;
        private ArrayOf<V2.ISA95JobOrderAndStateDataType> m_jobOrders = [];
        private ArrayOf<V2.ISA95JobResponseDataType> m_jobResponses = [];
        private bool m_predefinedParameters;
        private long m_refreshGeneration;
        private long m_appliedGeneration;
        private long m_responseRefreshGeneration;
        private long m_appliedResponseGeneration;
        private Task m_changeStreamTask = Task.CompletedTask;
        private ILogger? m_logger;
    }

    internal static partial class JobManagementBuilderLog
    {
        [LoggerMessage(
            EventId = MachineryServerEventIds.JobOrderChangeStreamFailed,
            Level = LogLevel.Error,
            Message = "The OPC 40001-3 job order change stream failed.")]
        public static partial void JobOrderChangeStreamFailed(
            this ILogger logger,
            Exception exception);
    }
}
