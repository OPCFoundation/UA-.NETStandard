// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.StateMachine;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Top-level runtime aggregator for a single PubSub
    /// application. Hosts the connections, the shared
    /// <see cref="IDataSetMetaDataRegistry"/>, and the root state
    /// machine all child components cascade from.
    /// </summary>
    /// <remarks>
    /// Implements the Application abstraction described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.2">
    /// Part 14 §9.1.2 PubSub address space root</see>.
    /// Exposes a runtime mutation API per Part 14 §9.1.6.
    /// </remarks>
    public interface IPubSubApplication : IAsyncDisposable
    {
        /// <summary>
        /// Application identifier.
        /// </summary>
        string ApplicationId { get; }

        /// <summary>
        /// Configured connections.
        /// </summary>
        // Live view over mutable internal list; ArrayOf would copy on every access.
        IReadOnlyList<IPubSubConnection> Connections { get; }

        /// <summary>
        /// Host-trusted metadata shared by connections. Received announcements
        /// remain in the receiving connection's metadata scope.
        /// </summary>
        IDataSetMetaDataRegistry MetaDataRegistry { get; }

        /// <summary>
        /// Root state machine.
        /// </summary>
        PubSubStateMachine State { get; }

        /// <summary>
        /// Per-component diagnostics aggregator (Part 14 §9.1.11).
        /// </summary>
        IPubSubDiagnostics Diagnostics { get; }

        /// <summary>
        /// Application configuration version (Part 14 §5.2.3).
        /// </summary>
        ConfigurationVersionDataType ConfigurationVersion { get; }

        /// <summary>
        /// Raised after any successful runtime configuration
        /// mutation.
        /// </summary>
        event EventHandler<PubSubConfigurationChangedEventArgs>?
            ConfigurationChanged;

        /// <summary>
        /// Starts the application.
        /// </summary>
        ValueTask StartAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops the application.
        /// </summary>
        ValueTask StopAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns a snapshot copy of the current configuration.
        /// </summary>
        PubSubConfigurationDataType GetConfiguration();

        /// <summary>
        /// Sends a PubSub discovery request on the application's active
        /// connections and collects responses until the timeout elapses.
        /// </summary>
        ValueTask<PubSubDiscoveryResult> RequestDiscoveryAsync(
            PubSubDiscoveryRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a PubSub Action request and awaits the correlated response.
        /// </summary>
        ValueTask<PubSubActionResponse> InvokeActionAsync(
            PubSubActionRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Registers a responder-side Action handler for a target.
        /// </summary>
        /// <param name="target">Action target handled by <paramref name="handler"/>.</param>
        /// <param name="handler">Action handler invoked for matching requests.</param>
        /// <param name="allowUnsecured">Allow serving the Action on an unsecured connection.</param>
        /// <param name="responseAddressPolicy">
        /// Optional policy that validates the requestor-supplied response address
        /// before a response is published (SA-ACT-03). When <see langword="null"/>
        /// the safe default (<see cref="PubSubResponseAddressPolicy.Default"/>) is
        /// used, which rejects arbitrary requestor topics on MQTT/JSON transports.
        /// </param>
        void RegisterActionHandler(
            PubSubActionTarget target,
            IPubSubActionHandler handler,
            bool allowUnsecured = false,
            PubSubResponseAddressPolicy? responseAddressPolicy = null);

        /// <summary>
        /// Clears all registered responder-side Action handlers so callers can
        /// rebuild the current registration set.
        /// </summary>
        void ClearActionHandlers();

        /// <summary>
        /// Replaces the entire configuration.
        /// </summary>
        ValueTask<ArrayOf<StatusCode>> ReplaceConfigurationAsync(
            PubSubConfigurationDataType configuration,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a new connection.
        /// </summary>
        ValueTask<NodeId> AddConnectionAsync(
            PubSubConnectionDataType configuration,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a connection by NodeId.
        /// </summary>
        ValueTask RemoveConnectionAsync(
            NodeId connectionId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a WriterGroup to a connection.
        /// </summary>
        ValueTask<NodeId> AddWriterGroupAsync(
            NodeId connectionId,
            WriterGroupDataType configuration,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a ReaderGroup to a connection.
        /// </summary>
        ValueTask<NodeId> AddReaderGroupAsync(
            NodeId connectionId,
            ReaderGroupDataType configuration,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a group by NodeId.
        /// </summary>
        ValueTask RemoveGroupAsync(
            NodeId groupId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a DataSetWriter to a WriterGroup.
        /// </summary>
        ValueTask<NodeId> AddDataSetWriterAsync(
            NodeId writerGroupId,
            DataSetWriterDataType configuration,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a DataSetWriter.
        /// </summary>
        ValueTask RemoveDataSetWriterAsync(
            NodeId writerId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a DataSetReader to a ReaderGroup.
        /// </summary>
        ValueTask<NodeId> AddDataSetReaderAsync(
            NodeId readerGroupId,
            DataSetReaderDataType configuration,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a DataSetReader.
        /// </summary>
        ValueTask RemoveDataSetReaderAsync(
            NodeId readerId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a PublishedDataSet.
        /// </summary>
        ValueTask<NodeId> AddPublishedDataSetAsync(
            PublishedDataSetDataType configuration,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a PublishedDataSet by NodeId.
        /// </summary>
        ValueTask RemovePublishedDataSetAsync(
            NodeId publishedDataSetId,
            CancellationToken cancellationToken = default);
    }
}
