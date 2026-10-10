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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Server;
using ResultBrowseNames = Opc.Ua.Machinery.Result.BrowseNames;

namespace Opc.Ua.Machinery.Server.Builders
{
    /// <summary>
    /// Assembles the OPC 40001-101 <c>ResultManagement</c> object of a machine.
    /// </summary>
    /// <remarks>
    /// All five methods of <c>ResultManagementType</c> are optional. The
    /// builder publishes <c>GetLatestResult</c>, <c>GetResultById</c> and
    /// <c>GetResultIdListFiltered</c> whenever a store is bound,
    /// <c>AcknowledgeResults</c> only when the store answers acknowledgements,
    /// and <c>ResultTransfer</c> only when downloads are enabled.
    /// </remarks>
    public interface IResultManagementBuilder
    {
        /// <summary>
        /// Gets the result-management state being configured.
        /// </summary>
        ResultManagementState State { get; }

        /// <summary>
        /// Binds the store the methods read from.
        /// </summary>
        /// <remarks>
        /// The machine's result publisher writes to the store when it
        /// implements <see cref="IWritableMachineryResultStore"/>. A store that
        /// fills itself through a path of its own is published from as well:
        /// the publisher then announces a result the store already holds.
        /// </remarks>
        /// <param name="store">The result store.</param>
        IResultManagementBuilder WithStore(IMachineryResultStore store);

        /// <summary>
        /// Binds an in-memory store and returns the publisher that feeds it, so
        /// a simulation can push results at runtime.
        /// </summary>
        /// <param name="capacity">How many results the store keeps.</param>
        IResultManagementBuilder WithInMemoryStore(int capacity = 64);

        /// <summary>
        /// Adds the <c>ResultTransfer</c> object and its
        /// <c>GenerateFileForRead</c> download path.
        /// </summary>
        IResultManagementBuilder WithFileTransfer();

        /// <summary>
        /// Advertises <c>Machinery-Result PredefinedResultMetaData</c> and
        /// enforces it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The unit requires <em>every</em> exposed result to set
        /// <c>ExternalRecipeId</c>, <c>InternalRecipeId</c>, <c>JobId</c>,
        /// <c>ProductId</c>, <c>StepId</c> and <c>CreationTime</c>. All six are
        /// optional fields of <c>ResultMetaDataType</c>, so only the
        /// application knows whether its machine fills them — hence the opt-in.
        /// </para>
        /// <para>
        /// Once opted in, publishing a result that leaves any of them unset is
        /// refused, and a result handed back by a store the application brought
        /// itself is refused as it leaves the store. The server therefore never
        /// advertises the unit while exposing a result that breaks it.
        /// </para>
        /// </remarks>
        IResultManagementBuilder WithPredefinedResultMetaData();

        /// <summary>
        /// Adds the <c>Results</c> folder and the result variables published
        /// in it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// OPC 40001-101's <c>ResultVariables</c> conformance unit asks for
        /// the folder to carry at least one <c>ResultType</c> variable, so an
        /// empty folder satisfies nothing. The variables are a fixed ring
        /// created with the rest of the machine: the newest published result
        /// lands in the first slot and the others shift down.
        /// </para>
        /// <para>
        /// A fixed ring rather than one node per result keeps the address
        /// space stable — a client can subscribe to a slot once and keep
        /// receiving whatever result currently occupies it, and the server
        /// never adds or deletes nodes while running.
        /// </para>
        /// </remarks>
        /// <param name="publishedResults">
        /// How many of the most recent results the folder publishes. Zero
        /// creates the folder alone, which is what a server that hands results
        /// out through the methods only wants.
        /// </param>
        IResultManagementBuilder WithResultsFolder(int publishedResults = 4);
    }

    /// <summary>
    /// Implements <see cref="IResultManagementBuilder"/>: stages the
    /// OPC 40001-101 <c>ResultManagement</c> add-in on a machine and, once the
    /// machine is registered, binds its methods, <c>ResultTransfer</c> and
    /// result-ready events to the result store and exposes the
    /// <see cref="IMachineryResultPublisher"/> that feeds them.
    /// </summary>
    internal sealed class ResultManagementBuilder : IResultManagementBuilder
    {
        public static ResultManagementBuilder Create(MachineryBuildScope scope, NodeState machine)
        {
            ushort namespaceIndex = MachineryBuilderUtilities.NamespaceIndex(
                scope.Context,
                Opc.Ua.Machinery.Result.Namespaces.MachineryResult);
            ResultManagementState management = MachineryBuilderUtilities.AddAddIn(
                scope.Context,
                machine,
                new QualifiedName(ResultBrowseNames.ResultManagement, namespaceIndex),
                static (ctx, parent, name) =>
                    ctx.CreateInstanceOfResultManagementType(parent, name));
            management.EventNotifier = EventNotifiers.SubscribeToEvents;
            scope.RecordFacet(MachineryFacet.ResultManagement);
            return new ResultManagementBuilder(scope, management);
        }

        private ResultManagementBuilder(MachineryBuildScope scope, ResultManagementState state)
        {
            m_scope = scope;
            State = state;
            m_binder = new MachineryResultManagementBinder(
                state,
                scope.Context,
                ResolveOptions(scope));
            scope.PostRegistrationActions.Add(BindAsync);
        }

        public ResultManagementState State { get; }

        public IMachineryResultPublisher? Publisher => m_publisher;

        public IResultManagementBuilder WithStore(IMachineryResultStore store)
        {
            m_scope.EnsureMutable();
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            return this;
        }

        public IResultManagementBuilder WithInMemoryStore(int capacity = 64)
        {
            m_scope.EnsureMutable();
            m_store = new InMemoryMachineryResultStore(capacity);
            return this;
        }

        public IResultManagementBuilder WithFileTransfer()
        {
            m_scope.EnsureMutable();
            State.AddResultTransfer(m_scope.Context);
            m_transferRequested = true;
            return this;
        }

        public IResultManagementBuilder WithPredefinedResultMetaData()
        {
            m_scope.EnsureMutable();
            m_predefinedMetaData = true;
            return this;
        }

        public IResultManagementBuilder WithResultsFolder(int publishedResults = 4)
        {
            if (publishedResults < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(publishedResults),
                    publishedResults,
                    "The number of published result variables cannot be negative.");
            }
            m_scope.EnsureMutable();
            State.AddResults(m_scope.Context);
            m_resultVariables = MachineryResultVariables.Create(
                m_scope,
                State.Results!,
                publishedResults);
            return this;
        }

        private async ValueTask BindAsync(CancellationToken cancellationToken)
        {
            IMachineryResultStore? store = m_store ??
                m_scope.BuildContext.GetService<IMachineryResultStore>();
            if (store == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "OPC 40001-101 ResultManagement needs a result store. Call " +
                    "WithStore() or WithInMemoryStore(), or register an " +
                    "IMachineryResultStore in the application services.");
            }

            // The publisher writes to the store the application bound; only
            // the read paths go through the metadata check below.
            IMachineryResultStore boundStore = store;
            if (m_predefinedMetaData)
            {
                // Wrapped before anything binds to it so every read path -
                // the three getters and the transfer manager - sees the check.
                store = new PredefinedResultMetaDataStore(store);
            }

            m_binder.BindMethods(store);
            if (m_scope.BuildContext.Manager is not AsyncCustomNodeManager manager)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "OPC 40001-101 ResultManagement requires an asynchronous custom " +
                    "node manager.");
            }

            // ResultReadyEventType is abstract; the concrete subtype the events
            // are reported with is registered before any result can be
            // published, so the publisher never has to check.
            await m_binder.BindEventTypeAsync(
                manager,
                m_scope.BuildContext.InstanceNamespaceIndex,
                cancellationToken).ConfigureAwait(false);

            BindTransfer(store);
            m_publisher = new ResultPublisher(this, boundStore);
            if (m_resultVariables is { Count: > 0 })
            {
                m_scope.RecordFacet(MachineryFacet.ResultVariables);
            }
            if (m_predefinedMetaData)
            {
                m_scope.RecordFacet(MachineryFacet.ResultPredefinedMetaData);
            }
        }

        private void BindTransfer(IMachineryResultStore store)
        {
            if (!m_transferRequested || State.ResultTransfer == null)
            {
                return;
            }
            if (m_scope.BuildContext.Manager is not AsyncCustomNodeManager manager)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Result transfer requires an asynchronous custom node manager.");
            }
            ILogger logger = m_scope.BuildContext.Manager.Server.Telemetry
                .CreateLogger<MachineryResultTransferManager>();
            MachineryResultTransferManager? transfer = m_binder.BindTransfer(
                manager,
                store,
                ResolveOptions(),
                logger);
            if (transfer != null)
            {
                m_scope.RegisteredResources.Add(transfer);
                m_scope.RecordFacet(MachineryFacet.ResultFiles);
            }
        }

        private MachineryServerOptions ResolveOptions() => ResolveOptions(m_scope);

        private static MachineryServerOptions ResolveOptions(MachineryBuildScope scope)
        {
            return scope.BuildContext is IMachineryBuildCoordinator coordinator
                ? coordinator.Options
                : new MachineryServerOptions();
        }

        internal ValueTask RaiseResultReadyAsync(
            MachineryResult result,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            m_resultVariables?.Publish(m_scope.Context, result);
            m_binder.RaiseResultReady(result);
            m_scope.RecordFacet(MachineryFacet.ResultEvents);
            return default;
        }

        private readonly MachineryBuildScope m_scope;
        private readonly MachineryResultManagementBinder m_binder;
        private IMachineryResultStore? m_store;
        private IMachineryResultPublisher? m_publisher;
        private MachineryResultVariables? m_resultVariables;
        private bool m_transferRequested;
        private bool m_predefinedMetaData;

        private sealed class ResultPublisher : IMachineryResultPublisher
        {
            public ResultPublisher(ResultManagementBuilder owner, IMachineryResultStore store)
            {
                m_owner = owner;
                m_store = store;
            }

            public async ValueTask PublishAsync(
                MachineryResult result,
                CancellationToken cancellationToken = default)
            {
                if (result == null)
                {
                    throw new ArgumentNullException(nameof(result));
                }
                if (m_owner.m_predefinedMetaData)
                {
                    PredefinedResultMetaData.Validate(result, onIngestion: true);
                }
                await MachineryResultIngestion
                    .StoreAsync(m_store, result, cancellationToken)
                    .ConfigureAwait(false);
                await m_owner.RaiseResultReadyAsync(result, cancellationToken)
                    .ConfigureAwait(false);
            }

            private readonly ResultManagementBuilder m_owner;
            private readonly IMachineryResultStore m_store;
        }
    }
}
