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
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Manages one in-process OPC UA PubSub publisher or subscriber for MCP tools.
    /// </summary>
    public sealed class PubSubRuntimeManager : IAsyncDisposable
    {
        private const string DataSetName = "McpDataSet";
        private const string WriterName = "Writer 1";
        private const string ReaderName = "Reader 1";
        private const ushort DataSetWriterId = 1;
        private const int DefaultPublishingIntervalMs = 100;
        private const int DefaultRingCapacity = 64;

        private readonly IServiceProvider m_services;
        private readonly ILogger<PubSubRuntimeManager> m_logger;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private IPubSubApplication? m_application;
        private MutablePublishedDataSetSource? m_source;
        private BufferedSubscribedDataSetSink? m_sink;
        private readonly List<PubSubActionResponderRegistration> m_actionResponders = [];

        private readonly List<(PubSubActionResponderRegistration Registration, IAsyncDisposable Binding)> m_bindings
            = [];

        private PubSubRuntimeMode m_mode = PubSubRuntimeMode.Stopped;
        private string m_endpoint = string.Empty;
        private ushort m_publisherId;
        private ushort m_writerGroupId;
        private McpCallerIdentity? m_owner;
        private bool m_disposed;
        private readonly Lock m_disposalState = new();
        private readonly CancellationTokenSource m_shutdown = new();
        private readonly TaskCompletionSource m_drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? m_disposal;
        private bool m_disposeRequested;
        private bool m_controlsDisposed;
        private int m_pendingUses;

        /// <summary>
        /// Initializes a new <see cref="PubSubRuntimeManager"/>.
        /// </summary>
        public PubSubRuntimeManager(IServiceProvider services, ILogger<PubSubRuntimeManager> logger)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(logger);

            m_services = services;
            m_logger = logger;
        }

        /// <summary>
        /// Starts an in-process UDP/UADP publisher.
        /// </summary>
        public async ValueTask<PubSubRuntimeStatus> StartPublisherAsync(
            string endpoint,
            ushort publisherId,
            ushort writerGroupId,
            string? fieldSpec,
            CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
            _ = RequireExecution();
            ArgumentOutOfRangeException.ThrowIfGreaterThan(fieldSpec?.Length ?? 0, 65536);

            List<RuntimeFieldDefinition> fields = ParseFieldSpec(fieldSpec);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(fields.Count, 64);
            MutablePublishedDataSetSource? source = null;
            try
            {
                source = new MutablePublishedDataSetSource(DataSetName, fields);
                IPubSubApplication app = CreateBuilder(
                        "urn:opcfoundation:OpcUaMcp:PubSubPublisher", endpoint, publisher: true)
                    .AddDataSetSource(DataSetName, source)
                    .UseConfiguration(BuildPublisherConfiguration(endpoint, publisherId, writerGroupId, fields))
                    .Build();

                await ReplaceAndStartAsync(app, PubSubRuntimeMode.Publisher, endpoint, publisherId, writerGroupId,
                    source, sink: null, ct).ConfigureAwait(false);
                source = null;
                return await StatusAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                source?.Dispose();
            }
        }

        /// <summary>
        /// Starts an in-process UDP/UADP subscriber with a bounded receive buffer.
        /// </summary>
        public async ValueTask<PubSubRuntimeStatus> StartSubscriberAsync(
            string endpoint,
            ushort publisherId,
            ushort writerGroupId,
            string? fieldSpec,
            CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
            _ = RequireExecution();
            ArgumentOutOfRangeException.ThrowIfGreaterThan(fieldSpec?.Length ?? 0, 65536);

            List<RuntimeFieldDefinition> fields = ParseFieldSpec(fieldSpec);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(fields.Count, 64);
            BufferedSubscribedDataSetSink? sink = null;
            try
            {
                sink = new BufferedSubscribedDataSetSink(DefaultRingCapacity);
                IPubSubApplication app = CreateBuilder(
                        "urn:opcfoundation:OpcUaMcp:PubSubSubscriber", endpoint, publisher: false)
                    .AddSubscribedDataSetSink(ReaderName, sink)
                    .UseConfiguration(BuildSubscriberConfiguration(endpoint, publisherId, writerGroupId, fields))
                    .Build();

                await ReplaceAndStartAsync(app, PubSubRuntimeMode.Subscriber, endpoint, publisherId, writerGroupId,
                    source: null, sink, ct).ConfigureAwait(false);
                sink = null;
                return await StatusAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                sink?.Dispose();
            }
        }

        /// <summary>
        /// Updates the active publisher's next sampled DataSet fields.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public async ValueTask<PubSubRuntimePublishResult> PublishAsync(
            string fieldValues,
            CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fieldValues);
            _ = RequireExecution();
            ArgumentOutOfRangeException.ThrowIfGreaterThan(fieldValues.Length, 65536);

            await EnterGateAsync(ct).ConfigureAwait(false);
            try
            {
                RequireOwner();
                if (m_mode != PubSubRuntimeMode.Publisher || m_source is null)
                {
                    throw new InvalidOperationException("Start a publisher with pubsub_runtime_start_publisher first.");
                }

                ArrayOf<PubSubRuntimeFieldValue> values = await m_source.UpdateAsync(fieldValues, ct)
                    .ConfigureAwait(false);
                return new PubSubRuntimePublishResult
                {
                    Mode = m_mode.ToString(),
                    Endpoint = m_endpoint,
                    PublisherId = m_publisherId,
                    WriterGroupId = m_writerGroupId,
                    Fields = values,
                    Message = "Field values updated; the publisher sends them on the next publishing interval."
                };
            }
            finally
            {
                ExitGate();
            }
        }

        /// <summary>
        /// Admits a legacy operation without letting shutdown dispose a semaphore with queued callers.
        /// </summary>
        private async Task EnterGateAsync(CancellationToken ct)
        {
            lock (m_disposalState)
            {
                ObjectDisposedException.ThrowIf(m_disposeRequested, this);
                m_pendingUses++;
            }
            try
            {
                await m_gate.WaitAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                ReleaseUse();
                throw;
            }
        }

        /// <summary>
        /// Releases an admitted operation before signaling that host disposal may complete.
        /// </summary>
        private void ExitGate()
        {
            m_gate.Release();
            ReleaseUse();
        }

        /// <summary>
        /// Completes the shutdown barrier after the last queued or executing caller leaves.
        /// </summary>
        private void ReleaseUse()
        {
            lock (m_disposalState)
            {
                m_pendingUses--;
                if (m_disposeRequested && m_pendingUses == 0)
                {
                    m_drained.TrySetResult();
                }
            }
        }

        /// <summary>
        /// Reads buffered DataSets from the active subscriber.
        /// </summary>
        public async ValueTask<ArrayOf<PubSubReceivedDataSet>> ReadReceivedAsync(
            bool clear,
            CancellationToken ct = default)
        {
            await EnterGateAsync(ct).ConfigureAwait(false);
            try
            {
                RequireOwner();
                return m_mode == PubSubRuntimeMode.Subscriber && m_sink is not null
                    ? await m_sink.ReadAsync(clear, ct).ConfigureAwait(false)
                    : [];
            }
            finally
            {
                ExitGate();
            }
        }

        /// <summary>
        /// Returns the current runtime status.
        /// </summary>
        public async ValueTask<PubSubRuntimeStatus> StatusAsync(CancellationToken ct = default)
        {
            await EnterGateAsync(ct).ConfigureAwait(false);
            try
            {
                RequireOwner();
                return CreateStatus();
            }
            finally
            {
                ExitGate();
            }
        }

        /// <summary>
        /// Sends a PubSub discovery request from the active runtime and
        /// collects the publisher responses within the timeout.
        /// </summary>
        /// <param name="request">The discovery request.</param>
        /// <param name="timeout">How long to collect responses.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The collected discovery result.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async ValueTask<PubSubDiscoveryResult> RequestDiscoveryAsync(
            PubSubDiscoveryRequest request,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, m_shutdown.Token);
            ct = lifetime.Token;
            _ = RequireExecution();
            ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout, TimeSpan.FromSeconds(30));
            await EnterGateAsync(ct).ConfigureAwait(false);
            try
            {
                RequireOwner();
                IPubSubApplication app = m_application ??
                    throw new InvalidOperationException(
                        "No PubSub runtime is active. Start a publisher or subscriber first.");
                return await app.RequestDiscoveryAsync(request, timeout, ct).ConfigureAwait(false);
            }
            finally
            {
                ExitGate();
            }
        }

        /// <summary>
        /// Sends a PubSub Action request from the active runtime and awaits the correlated response.
        /// </summary>
        /// <param name="request">The Action request.</param>
        /// <param name="timeout">How long to wait for the response.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The correlated Action response.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async ValueTask<PubSubActionResponse> InvokeActionAsync(
            PubSubActionRequest request,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, m_shutdown.Token);
            ct = lifetime.Token;
            _ = RequireExecution();
            ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout, TimeSpan.FromSeconds(30));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(request.InputFields.Count, 64);
            await EnterGateAsync(ct).ConfigureAwait(false);
            try
            {
                RequireOwner();
                IPubSubApplication app = m_application ??
                    throw new InvalidOperationException(
                        "No PubSub runtime is active. Start a publisher or subscriber first.");
                return await app.InvokeActionAsync(request, timeout, ct).ConfigureAwait(false);
            }
            finally
            {
                ExitGate();
            }
        }

        /// <summary>
        /// Registers a responder-side Action handler on the active runtime.
        /// </summary>
        /// <param name="target">The Action target.</param>
        /// <param name="handler">The Action handler.</param>
        /// <param name="responderKind">The JSON-friendly responder kind.</param>
        /// <param name="details">The JSON-friendly responder details.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The registered responder information.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public ValueTask<PubSubActionResponderRegistration> RegisterActionResponderAsync(
            PubSubActionTarget target,
            IPubSubActionHandler handler,
            string responderKind,
            string details,
            CancellationToken ct = default)
        {
            return RegisterActionResponderCoreAsync(target, handler, responderKind, details, null, ct);
        }

        /// <summary>
        /// Transfers ownership of an explicitly scoped method binding only after registration succeeds.
        /// </summary>
        internal ValueTask<PubSubActionResponderRegistration> RegisterBoundActionResponderAsync(
            PubSubActionTarget target, IPubSubActionHandler handler, string details,
            IAsyncDisposable binding, CancellationToken ct)
        {
            return RegisterActionResponderCoreAsync(target, handler, "method", details, binding, ct);
        }

        /// <summary>
        /// Applies common legacy registration, ownership and bounded-responder rules.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        private async ValueTask<PubSubActionResponderRegistration> RegisterActionResponderCoreAsync(
            PubSubActionTarget target, IPubSubActionHandler handler, string responderKind, string details,
            IAsyncDisposable? binding, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentException.ThrowIfNullOrWhiteSpace(responderKind);
            _ = RequireExecution();

            var registration = new PubSubActionResponderRegistration(
                target.ConnectionName,
                target.DataSetWriterId,
                target.ActionTargetId,
                target.ActionName,
                responderKind,
                details ?? string.Empty);

            await EnterGateAsync(ct).ConfigureAwait(false);
            try
            {
                RequireOwner();
                if (m_actionResponders.Count >= 64 && !m_actionResponders.Any(item => item.MatchesTarget(registration)))
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations,
                        "The legacy runtime's responder quota is full.");
                }
                IPubSubApplication app = m_application ??
                    throw new InvalidOperationException(
                        "No PubSub runtime is active. Start a publisher or subscriber first.");
                // The MCP runtime is a local diagnostic surface that binds Action
                // responders onto connections that are typically unsecured, so it
                // opts in explicitly (SA-ACT-01). This makes serving Action requests
                // without message security a deliberate, auditable choice rather than
                // a silent default.
                app.RegisterActionHandler(target, handler, allowUnsecured: true);
                foreach ((PubSubActionResponderRegistration previous, IAsyncDisposable owned) in m_bindings
                    .Where(item => item.Registration.MatchesTarget(registration)).ToArray())
                {
                    await owned.DisposeAsync().ConfigureAwait(false);
                    m_bindings.Remove((previous, owned));
                }
                m_actionResponders.RemoveAll(item => item.MatchesTarget(registration));
                m_actionResponders.Add(registration);
                if (binding is not null)
                {
                    m_bindings.Add((registration, binding));
                }
                return registration;
            }
            finally
            {
                ExitGate();
            }
        }

        /// <summary>
        /// Lists Action targets known from the active configuration and registered responders.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The JSON-friendly Action target list.</returns>
        public async ValueTask<ArrayOf<PubSubActionTargetInfo>> ListActionTargetsAsync(
            CancellationToken ct = default)
        {
            await EnterGateAsync(ct).ConfigureAwait(false);
            try
            {
                RequireOwner();
                var targets = new List<PubSubActionTargetInfo>();
                if (m_application is not null)
                {
                    AddConfiguredActionTargets(m_application.GetConfiguration(), targets);
                }

                foreach (PubSubActionResponderRegistration registration in m_actionResponders)
                {
                    if (!targets.Any(registration.MatchesTarget))
                    {
                        targets.Add(registration.ToTargetInfo("registered-responder", string.Empty, string.Empty));
                    }
                }

                return [.. targets];
            }
            finally
            {
                ExitGate();
            }
        }

        /// <summary>
        /// Lists Action responders registered through MCP.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The JSON-friendly responder list.</returns>
        public async ValueTask<ArrayOf<PubSubActionResponderRegistration>> ListActionRespondersAsync(
            CancellationToken ct = default)
        {
            await EnterGateAsync(ct).ConfigureAwait(false);
            try
            {
                RequireOwner();
                return [.. m_actionResponders];
            }
            finally
            {
                ExitGate();
            }
        }

        /// <summary>
        /// Stops and disposes the active PubSub application.
        /// </summary>
        public async ValueTask<PubSubRuntimeStatus> StopAsync(CancellationToken ct = default)
        {
            await EnterGateAsync(ct).ConfigureAwait(false);
            try
            {
                RequireOwner();
                await StopCurrentAsync(ct).ConfigureAwait(false);
                return CreateStatus();
            }
            finally
            {
                ExitGate();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Task disposal;
            lock (m_disposalState)
            {
                if (m_disposal is null)
                {
                    m_disposeRequested = true;
                    if (m_pendingUses == 0)
                    {
                        m_drained.TrySetResult();
                    }
                    m_disposal = DisposeCoreAsync();
                }
                disposal = m_disposal;
            }
            try
            {
                await disposal.ConfigureAwait(false);
            }
            finally
            {
                lock (m_disposalState)
                {
                    if (!m_controlsDisposed)
                    {
                        m_gate.Dispose();
                        m_shutdown.Dispose();
                        m_controlsDisposed = true;
                    }
                }
            }
        }

        /// <summary>
        /// Cancels activation and drains admitted callers before disposing the private gate.
        /// </summary>
        private async Task DisposeCoreAsync()
        {
            await m_shutdown.CancelAsync().ConfigureAwait(false);
            await m_drained.Task.ConfigureAwait(false);
            await m_gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!m_disposed)
                {
                    await StopCurrentAsync(CancellationToken.None).ConfigureAwait(false);
                    m_disposed = true;
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        private async ValueTask ReplaceAndStartAsync(
            IPubSubApplication app,
            PubSubRuntimeMode mode,
            string endpoint,
            ushort publisherId,
            ushort writerGroupId,
            MutablePublishedDataSetSource? source,
            BufferedSubscribedDataSetSink? sink,
            CancellationToken ct)
        {
            CancellationTokenSource? lifetime = null;
            bool acquired = false;
            try
            {
                lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, m_shutdown.Token);
                ct = lifetime.Token;
                await EnterGateAsync(ct).ConfigureAwait(false);
                acquired = true;
                ObjectDisposedException.ThrowIf(m_disposed, this);
                McpCallerIdentity caller = RequireExecution();
                RequireOwner();
                await StopCurrentAsync(ct).ConfigureAwait(false);
                await app.StartAsync(ct).ConfigureAwait(false);
                m_application = app;
                m_mode = mode;
                m_endpoint = endpoint;
                m_publisherId = publisherId;
                m_writerGroupId = writerGroupId;
                m_source = source;
                m_sink = sink;
                m_owner = caller;
            }
            catch
            {
                await app.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                lifetime?.Dispose();
                if (acquired)
                {
                    ExitGate();
                }
            }
        }

        private PubSubApplicationBuilder CreateBuilder(string applicationId, string endpoint, bool publisher)
        {
            var telemetry = new ServiceProviderTelemetryContext(m_services);
            if (m_services.GetService<IMcpPubSubTransportProvider>() is not { } provider)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "The legacy UDP runtime requires the host's policy-controlled PubSub transport provider.");
            }
            return new PubSubApplicationBuilder(telemetry)
                .WithApplicationId(applicationId)
                .AddTransportFactory(provider.CreateLegacyFactory(endpoint, publisher))
                .AddEncoder(new UadpEncoder())
                .AddDecoder(new UadpDecoder());
        }

        private async ValueTask StopCurrentAsync(CancellationToken ct)
        {
            IPubSubApplication? app = m_application;
            MutablePublishedDataSetSource? source = m_source;
            BufferedSubscribedDataSetSink? sink = m_sink;
            if (app is not null)
            {
                try
                {
                    await app.StopAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    m_logger.StoppingPubSubRuntimeFailed(ex);
                }
                await app.DisposeAsync().ConfigureAwait(false);
            }
            foreach ((_, IAsyncDisposable binding) in m_bindings)
            {
                await binding.DisposeAsync().ConfigureAwait(false);
            }
            m_bindings.Clear();
            source?.Dispose();
            sink?.Dispose();
            m_application = null;
            m_source = null;
            m_sink = null;
            m_mode = PubSubRuntimeMode.Stopped;
            m_endpoint = string.Empty;
            m_publisherId = 0;
            m_writerGroupId = 0;
            m_owner = null;
            m_actionResponders.Clear();
        }

        /// <summary>
        /// Requires explicit host/caller opt-in, including for the legacy catalogue and Full.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private McpCallerIdentity RequireExecution()
        {
            return (m_services.GetService<IMcpOperationPolicy>() ??
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                    "The host must explicitly configure PubSubRuntime authority before legacy live execution."))
                .Require(McpCapability.PubSubRuntime);
        }

        /// <summary>
        /// Captures exact host endpoint/node/method permission and the shared protected-target guard.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask<McpPubSubBorrowedSession> CreateMethodBindingAsync(
            OpcUaSessionManager sessions, OpcUaSessionManager.SessionInfo info, NodeId objectId, NodeId methodId,
            CancellationToken ct)
        {
            _ = sessions.OperationPolicy.RequireSession(McpCapability.PubSubBridge, info.Name);
            McpPubSubOptions options = m_services.GetRequiredService<McpPubSubOptions>();
            if (info.Endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                !options.AdapterRules.ToList().Any(rule => rule.Operation == "call" &&
                    rule.SessionName == info.Name &&
                    rule.Endpoint == info.Endpoint.EndpointUrl &&
                    McpPubSubRemote.Node(info.Session, rule.NodeId) == objectId &&
                    rule.MethodId is not null &&
                    McpPubSubRemote.Node(info.Session, rule.MethodId) == methodId))
            {
                throw McpPubSubTransportProvider.Refused();
            }
            await sessions.UaOperationGuard.AuthorizeCallsAsync(info,
                [new CallMethodRequest { ObjectId = objectId, MethodId = methodId }], ct).ConfigureAwait(false);
            return new McpPubSubBorrowedSession(sessions, info, "call", [objectId], options,
                m_services.GetService<TimeProvider>() ?? TimeProvider.System, methodId);
        }

        /// <summary>
        /// Prevents another caller from reading, replacing or stopping the legacy default.
        /// Its lifetime remains until explicit stop or host disposal, not a named-runtime lease.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void RequireOwner()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            if (m_owner is not null &&
                m_services.GetRequiredService<IMcpOperationPolicy>().RequireCaller() != m_owner)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                    "The legacy default PubSub runtime belongs to another caller.");
            }
        }

        private PubSubRuntimeStatus CreateStatus()
        {
            return new PubSubRuntimeStatus
            {
                Mode = m_mode.ToString(),
                IsRunning = m_application is not null,
                Endpoint = m_endpoint,
                PublisherId = m_publisherId,
                WriterGroupId = m_writerGroupId,
                BufferedDataSetCount = m_sink?.Count ?? 0
            };
        }

        private static void AddConfiguredActionTargets(
            PubSubConfigurationDataType configuration,
            List<PubSubActionTargetInfo> targets)
        {
            var actionDataSets = new Dictionary<string, PublishedActionDataType>(StringComparer.Ordinal);
            if (!configuration.PublishedDataSets.IsNull)
            {
                foreach (PublishedDataSetDataType publishedDataSet in configuration.PublishedDataSets)
                {
                    if (publishedDataSet is not null &&
                        TryGetPublishedAction(publishedDataSet, out PublishedActionDataType? action))
                    {
                        actionDataSets[publishedDataSet.Name ?? string.Empty] = action!;
                    }
                }
            }

            if (configuration.Connections.IsNull)
            {
                return;
            }

            foreach (PubSubConnectionDataType connection in configuration.Connections)
            {
                if (connection?.WriterGroups.IsNull != false)
                {
                    continue;
                }

                foreach (WriterGroupDataType writerGroup in connection.WriterGroups)
                {
                    if (writerGroup?.DataSetWriters.IsNull != false)
                    {
                        continue;
                    }

                    foreach (DataSetWriterDataType writer in writerGroup.DataSetWriters)
                    {
                        string dataSetName = writer?.DataSetName ?? string.Empty;
                        if (writer is null ||
                            !actionDataSets.TryGetValue(dataSetName, out PublishedActionDataType? action) ||
                            action.ActionTargets.IsNull)
                        {
                            continue;
                        }

                        foreach (ActionTargetDataType actionTarget in action.ActionTargets)
                        {
                            if (actionTarget is null)
                            {
                                continue;
                            }

                            targets.Add(new PubSubActionTargetInfo(
                                connection.Name ?? string.Empty,
                                writer.DataSetWriterId,
                                actionTarget.ActionTargetId,
                                actionTarget.Name ?? string.Empty,
                                writer.Name ?? string.Empty,
                                dataSetName,
                                actionTarget.Description.IsNull
                                    ? string.Empty
                                    : actionTarget.Description.Text ?? string.Empty,
                                "configuration"));
                        }
                    }
                }
            }
        }

        private static bool TryGetPublishedAction(
            PublishedDataSetDataType publishedDataSet,
            out PublishedActionDataType? action)
        {
            action = null;
            if (publishedDataSet.DataSetSource.IsNull)
            {
                return false;
            }

            if (publishedDataSet.DataSetSource.TryGetValue(out PublishedActionMethodDataType? methodAction))
            {
                action = methodAction;
                return true;
            }

            if (publishedDataSet.DataSetSource.TryGetValue(out PublishedActionDataType? publishedAction))
            {
                action = publishedAction;
                return true;
            }

            return false;
        }

        private static PubSubConfigurationDataType BuildPublisherConfiguration(
            string endpoint,
            ushort publisherId,
            ushort writerGroupId,
            List<RuntimeFieldDefinition> fields)
        {
            return PubSubConfigurationBuilder.Create()
                .AddPublishedDataSet(DataSetName, ds =>
                {
                    foreach (RuntimeFieldDefinition field in fields)
                    {
                        ds.AddField(field.Name, field.BuiltInType, field.DataTypeId);
                    }
                })
                .AddConnection("MCP Publisher Connection", connection => connection
                        .WithPublisherId(new Variant(publisherId))
                        .WithTransportProfile(Profiles.PubSubUdpUadpTransport)
                        .WithAddress(endpoint)
                        .AddWriterGroup("WriterGroup 1", group => group
                            .WithWriterGroupId(writerGroupId)
                            .WithPublishingInterval(DefaultPublishingIntervalMs)
                            .WithMessageSettings(CreateWriterGroupMessageSettings())
                            .WithTransportSettings(new DatagramWriterGroupTransportDataType())
                            .AddDataSetWriter(WriterName, writer => writer
                                .WithDataSetWriterId(DataSetWriterId)
                                .WithDataSetName(DataSetName)
                                .WithKeyFrameCount(1)
                                .WithFieldContentMask(DataSetFieldContentMask.RawData)
                                .WithMessageSettings(CreateWriterMessageSettings()))))
                .Build();
        }

        private static PubSubConfigurationDataType BuildSubscriberConfiguration(
            string endpoint,
            ushort publisherId,
            ushort writerGroupId,
            List<RuntimeFieldDefinition> fields)
        {
            return PubSubConfigurationBuilder.Create()
                .AddConnection("MCP Subscriber Connection", connection => connection
                        .WithPublisherId(new Variant(publisherId))
                        .WithTransportProfile(Profiles.PubSubUdpUadpTransport)
                        .WithAddress(endpoint)
                        .AddReaderGroup("ReaderGroup 1", group => group
                            .WithMaxNetworkMessageSize(1500)
                            .AddDataSetReader(ReaderName, reader => reader
                                    .WithFilter(new Variant(publisherId), writerGroupId, DataSetWriterId)
                                    .WithFieldContentMask(DataSetFieldContentMask.RawData)
                                    .WithMessageReceiveTimeout(5000)
                                    .WithMessageSettings(CreateReaderMessageSettings())
                                    .WithMirrorSubscribedDataSet(ReaderName)
                                    .WithDataSetMetaData(DataSetName, metaData =>
                                    {
                                        metaData.WithoutFieldIds();
                                        foreach (RuntimeFieldDefinition field in fields)
                                        {
                                            metaData.AddField(field.Name, field.BuiltInType, field.DataTypeId);
                                        }
                                    }))))
                .Build();
        }

        private static UadpWriterGroupMessageDataType CreateWriterGroupMessageSettings()
        {
            return new UadpWriterGroupMessageDataType
            {
                DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                NetworkMessageContentMask = (uint)(
                    UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.PayloadHeader |
                    UadpNetworkMessageContentMask.NetworkMessageNumber |
                    UadpNetworkMessageContentMask.SequenceNumber)
            };
        }

        private static UadpDataSetWriterMessageDataType CreateWriterMessageSettings()
        {
            return new UadpDataSetWriterMessageDataType
            {
                DataSetMessageContentMask = (uint)(
                    UadpDataSetMessageContentMask.Status |
                    UadpDataSetMessageContentMask.SequenceNumber)
            };
        }

        private static UadpDataSetReaderMessageDataType CreateReaderMessageSettings()
        {
            return new UadpDataSetReaderMessageDataType
            {
                NetworkMessageContentMask = (uint)(
                    UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.PayloadHeader |
                    UadpNetworkMessageContentMask.NetworkMessageNumber |
                    UadpNetworkMessageContentMask.SequenceNumber),
                DataSetMessageContentMask = (uint)(
                    UadpDataSetMessageContentMask.Status |
                    UadpDataSetMessageContentMask.SequenceNumber)
            };
        }

        private static List<RuntimeFieldDefinition> ParseFieldSpec(string? fieldSpec)
        {
            if (string.IsNullOrWhiteSpace(fieldSpec))
            {
                return
                [
                    RuntimeFieldDefinition.Create("BoolToggle", "Boolean"),
                    RuntimeFieldDefinition.Create("Int32", "Int32"),
                    RuntimeFieldDefinition.Create("DateTime", "DateTime")
                ];
            }

            var fields = new List<RuntimeFieldDefinition>();
            foreach (string entry in fieldSpec.Split(
                [';', ','],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = entry.Split(':', 2, StringSplitOptions.TrimEntries);
                fields.Add(RuntimeFieldDefinition.Create(parts[0], parts.Length == 2 ? parts[1] : "String"));
            }

            if (fields.Count == 0)
            {
                throw new ArgumentException("At least one field must be specified.", nameof(fieldSpec));
            }

            return fields;
        }

        private sealed class MutablePublishedDataSetSource : IPublishedDataSetSource, IDisposable
        {
            private readonly string m_name;
            private readonly List<RuntimeFieldDefinition> m_definitions;
            private readonly Dictionary<string, RuntimeFieldDefinition> m_definitionByName;
            private readonly Dictionary<string, Variant> m_values;
            private readonly SemaphoreSlim m_valueGate = new(1, 1);
            private uint m_minorVersion;

            public MutablePublishedDataSetSource(string name, List<RuntimeFieldDefinition> definitions)
            {
                m_name = name;
                m_definitions = definitions;
                m_definitionByName = definitions.ToDictionary(field => field.Name, StringComparer.Ordinal);
                m_values = definitions.ToDictionary(
                    field => field.Name,
                    field => field.CreateDefaultValue(),
                    StringComparer.Ordinal);
            }

            public DataSetMetaDataType BuildMetaData()
            {
                var fields = new List<FieldMetaData>();
                foreach (RuntimeFieldDefinition field in m_definitions)
                {
                    fields.Add(new FieldMetaData
                    {
                        Name = field.Name,
                        BuiltInType = field.BuiltInType,
                        DataType = field.DataTypeId,
                        ValueRank = ValueRanks.Scalar
                    });
                }

                return new DataSetMetaDataType
                {
                    Name = m_name,
                    DataSetClassId = Uuid.Empty,
                    Fields = new ArrayOf<FieldMetaData>(fields.ToArray()),
                    ConfigurationVersion = new ConfigurationVersionDataType
                    {
                        MajorVersion = 1,
                        MinorVersion = m_minorVersion
                    }
                };
            }

            public async ValueTask<PublishedDataSetSnapshot> SampleAsync(
                DataSetMetaDataType metaData,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await m_valueGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var fields = new List<DataSetField>();
                    foreach (RuntimeFieldDefinition definition in m_definitions)
                    {
                        fields.Add(new DataSetField
                        {
                            Name = definition.Name,
                            Value = m_values[definition.Name]
                        });
                    }

                    ConfigurationVersionDataType version = metaData?.ConfigurationVersion
                        ?? new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = m_minorVersion };
                    return new PublishedDataSetSnapshot(
                        version,
                        new ArrayOf<DataSetField>(fields.ToArray()),
                        DateTimeUtc.From(DateTimeOffset.UtcNow));
                }
                finally
                {
                    m_valueGate.Release();
                }
            }

            public async ValueTask<ArrayOf<PubSubRuntimeFieldValue>> UpdateAsync(
                string fieldValues,
                CancellationToken ct)
            {
                Dictionary<string, string> parsed = ParseFieldValues(fieldValues);
                await m_valueGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    foreach (KeyValuePair<string, string> item in parsed)
                    {
                        if (!m_definitionByName.TryGetValue(item.Key, out RuntimeFieldDefinition? definition))
                        {
                            throw new ArgumentException(
                                $"Unknown field '{item.Key}'. Add it to fieldSpec before starting the publisher.",
                                nameof(fieldValues));
                        }
                        m_values[item.Key] = definition.ParseValue(item.Value);
                    }
                    m_minorVersion++;
                    return new ArrayOf<PubSubRuntimeFieldValue>(m_definitions
                        .Select(field => new PubSubRuntimeFieldValue
                        {
                            Name = field.Name,
                            BuiltInType = field.BuiltInTypeName,
                            Value = m_values[field.Name].ToString()
                        })
                        .ToArray());
                }
                finally
                {
                    m_valueGate.Release();
                }
            }

            public void Dispose()
            {
                m_valueGate.Dispose();
            }

            private static Dictionary<string, string> ParseFieldValues(string fieldValues)
            {
                string trimmed = fieldValues.Trim();
                if (trimmed.StartsWith('{'))
                {
                    using var document = JsonDocument.Parse(trimmed);
                    var values = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (JsonProperty property in document.RootElement.EnumerateObject())
                    {
                        values[property.Name] = ElementToString(property.Value);
                    }
                    return values;
                }

                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string entry in trimmed.Split(
                    [';', ','],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string[] parts = entry.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (parts.Length != 2)
                    {
                        throw new ArgumentException(
                            "Field values must be JSON object text or name=value pairs separated by ';'.",
                            nameof(fieldValues));
                    }
                    result[parts[0]] = parts[1];
                }
                return result;
            }

            private static string ElementToString(JsonElement element)
            {
                return element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString() ?? string.Empty,
                    JsonValueKind.Number => element.GetRawText(),
                    JsonValueKind.True => bool.TrueString,
                    JsonValueKind.False => bool.FalseString,
                    JsonValueKind.Null => string.Empty,
                    _ => element.GetRawText()
                };
            }
        }

        private sealed class BufferedSubscribedDataSetSink : ISubscribedDataSetSink, IDisposable
        {
            private readonly int m_capacity;
            private readonly Queue<PubSubReceivedDataSet> m_received;
            private readonly SemaphoreSlim m_bufferGate = new(1, 1);
            private long m_sequence;

            public BufferedSubscribedDataSetSink(int capacity)
            {
                m_capacity = capacity;
                m_received = new Queue<PubSubReceivedDataSet>(capacity);
            }

            public int Count => m_received.Count;

            public async ValueTask WriteAsync(
                IReadOnlyList<DataSetField> fields,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = new List<PubSubRuntimeFieldValue>();
                for (int i = 0; i < fields.Count; i++)
                {
                    DataSetField field = fields[i];
                    values.Add(new PubSubRuntimeFieldValue
                    {
                        Name = string.IsNullOrEmpty(field.Name)
                            ? string.Create(CultureInfo.InvariantCulture, $"f{i}")
                            : field.Name,
                        BuiltInType = field.Value.TypeInfo.BuiltInType.ToString(),
                        Value = field.Value.IsNull ? string.Empty : field.Value.ToString()
                    });
                }

                var received = new PubSubReceivedDataSet
                {
                    Sequence = Interlocked.Increment(ref m_sequence),
                    ReceivedAt = DateTimeOffset.UtcNow,
                    Fields = new ArrayOf<PubSubRuntimeFieldValue>(values.ToArray())
                };

                await m_bufferGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    while (m_received.Count >= m_capacity)
                    {
                        m_received.Dequeue();
                    }
                    m_received.Enqueue(received);
                }
                finally
                {
                    m_bufferGate.Release();
                }
            }

            public async ValueTask<ArrayOf<PubSubReceivedDataSet>> ReadAsync(bool clear, CancellationToken ct)
            {
                await m_bufferGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var snapshot = new ArrayOf<PubSubReceivedDataSet>(m_received.ToArray());
                    if (clear)
                    {
                        m_received.Clear();
                    }
                    return snapshot;
                }
                finally
                {
                    m_bufferGate.Release();
                }
            }

            public void Dispose()
            {
                m_bufferGate.Dispose();
            }
        }

        private sealed record RuntimeFieldDefinition(
            string Name,
            string BuiltInTypeName,
            byte BuiltInType,
            NodeId DataTypeId)
        {
            public static RuntimeFieldDefinition Create(string name, string builtInTypeName)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name);
                ArgumentException.ThrowIfNullOrWhiteSpace(builtInTypeName);

                return builtInTypeName.Trim().ToLowerInvariant() switch
                {
                    "boolean" or "bool" => new(name, "Boolean", (byte)DataTypes.Boolean, DataTypeIds.Boolean),
                    "sbyte" => new(name, "SByte", (byte)DataTypes.SByte, DataTypeIds.SByte),
                    "byte" => new(name, "Byte", (byte)DataTypes.Byte, DataTypeIds.Byte),
                    "int16" => new(name, "Int16", (byte)DataTypes.Int16, DataTypeIds.Int16),
                    "uint16" => new(name, "UInt16", (byte)DataTypes.UInt16, DataTypeIds.UInt16),
                    "int32" or "int" => new(name, "Int32", (byte)DataTypes.Int32, DataTypeIds.Int32),
                    "uint32" => new(name, "UInt32", (byte)DataTypes.UInt32, DataTypeIds.UInt32),
                    "int64" or "long" => new(name, "Int64", (byte)DataTypes.Int64, DataTypeIds.Int64),
                    "uint64" => new(name, "UInt64", (byte)DataTypes.UInt64, DataTypeIds.UInt64),
                    "float" => new(name, "Float", (byte)DataTypes.Float, DataTypeIds.Float),
                    "double" => new(name, "Double", (byte)DataTypes.Double, DataTypeIds.Double),
                    "datetime" => new(name, "DateTime", (byte)DataTypes.DateTime, DataTypeIds.DateTime),
                    "string" => new(name, "String", (byte)DataTypes.String, DataTypeIds.String),
                    _ => throw new ArgumentException(
                        $"Unsupported PubSub field type '{builtInTypeName}'.",
                        nameof(builtInTypeName))
                };
            }

            public Variant CreateDefaultValue()
            {
                return BuiltInTypeName switch
                {
                    "Boolean" => new Variant(false),
                    "SByte" => new Variant((sbyte)0),
                    "Byte" => new Variant((byte)0),
                    "Int16" => new Variant((short)0),
                    "UInt16" => new Variant((ushort)0),
                    "Int32" => new Variant(0),
                    "UInt32" => new Variant(0u),
                    "Int64" => new Variant(0L),
                    "UInt64" => new Variant(0UL),
                    "Float" => new Variant(0f),
                    "Double" => new Variant(0d),
                    "DateTime" => new Variant(DateTime.UtcNow),
                    _ => new Variant(string.Empty)
                };
            }

            public Variant ParseValue(string text)
            {
                return BuiltInTypeName switch
                {
                    "Boolean" => new Variant(bool.Parse(text)),
                    "SByte" => new Variant(sbyte.Parse(text, CultureInfo.InvariantCulture)),
                    "Byte" => new Variant(byte.Parse(text, CultureInfo.InvariantCulture)),
                    "Int16" => new Variant(short.Parse(text, CultureInfo.InvariantCulture)),
                    "UInt16" => new Variant(ushort.Parse(text, CultureInfo.InvariantCulture)),
                    "Int32" => new Variant(int.Parse(text, CultureInfo.InvariantCulture)),
                    "UInt32" => new Variant(uint.Parse(text, CultureInfo.InvariantCulture)),
                    "Int64" => new Variant(long.Parse(text, CultureInfo.InvariantCulture)),
                    "UInt64" => new Variant(ulong.Parse(text, CultureInfo.InvariantCulture)),
                    "Float" => new Variant(float.Parse(text, CultureInfo.InvariantCulture)),
                    "Double" => new Variant(double.Parse(text, CultureInfo.InvariantCulture)),
                    "DateTime" => new Variant(DateTime.Parse(text, CultureInfo.InvariantCulture).ToUniversalTime()),
                    _ => new Variant(text)
                };
            }
        }
    }

    /// <summary>
    /// Current PubSub runtime mode.
    /// </summary>
    public enum PubSubRuntimeMode
    {
        /// <summary>
        /// No PubSub application is running.
        /// </summary>
        Stopped,

        /// <summary>
        /// A publisher application is running.
        /// </summary>
        Publisher,

        /// <summary>
        /// A subscriber application is running.
        /// </summary>
        Subscriber
    }

    /// <summary>
    /// Current runtime status.
    /// </summary>
    public sealed class PubSubRuntimeStatus
    {
        /// <summary>
        /// Gets whether an application is running.
        /// </summary>
        public bool IsRunning { get; init; }

        /// <summary>
        /// Gets the current mode.
        /// </summary>
        public string Mode { get; init; } = string.Empty;

        /// <summary>
        /// Gets the transport endpoint.
        /// </summary>
        public string Endpoint { get; init; } = string.Empty;

        /// <summary>
        /// Gets the publisher id.
        /// </summary>
        public ushort PublisherId { get; init; }

        /// <summary>
        /// Gets the writer group id.
        /// </summary>
        public ushort WriterGroupId { get; init; }

        /// <summary>
        /// Gets the number of buffered received DataSets.
        /// </summary>
        public int BufferedDataSetCount { get; init; }
    }

    /// <summary>
    /// Result of updating the active publisher's fields.
    /// </summary>
    public sealed class PubSubRuntimePublishResult
    {
        /// <summary>
        /// Gets the current mode.
        /// </summary>
        public string Mode { get; init; } = string.Empty;

        /// <summary>
        /// Gets the transport endpoint.
        /// </summary>
        public string Endpoint { get; init; } = string.Empty;

        /// <summary>
        /// Gets the publisher id.
        /// </summary>
        public ushort PublisherId { get; init; }

        /// <summary>
        /// Gets the writer group id.
        /// </summary>
        public ushort WriterGroupId { get; init; }

        /// <summary>
        /// Gets the fields that will be sampled by the publisher.
        /// </summary>
        public ArrayOf<PubSubRuntimeFieldValue> Fields { get; init; } = [];

        /// <summary>
        /// Gets a status message.
        /// </summary>
        public string Message { get; init; } = string.Empty;
    }

    /// <summary>
    /// A JSON-friendly PubSub field value.
    /// </summary>
    public sealed class PubSubRuntimeFieldValue
    {
        /// <summary>
        /// Gets the field name.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Gets the OPC UA built-in type name.
        /// </summary>
        public string BuiltInType { get; init; } = string.Empty;

        /// <summary>
        /// Gets the field value as text.
        /// </summary>
        public string Value { get; init; } = string.Empty;
    }

    /// <summary>
    /// One buffered DataSet received by the subscriber.
    /// </summary>
    public sealed class PubSubReceivedDataSet
    {
        /// <summary>
        /// Gets the local receive sequence.
        /// </summary>
        public long Sequence { get; init; }

        /// <summary>
        /// Gets the receive timestamp.
        /// </summary>
        public DateTimeOffset ReceivedAt { get; init; }

        /// <summary>
        /// Gets the received fields.
        /// </summary>
        public ArrayOf<PubSubRuntimeFieldValue> Fields { get; init; } = [];
    }

    /// <summary>
    /// A JSON-friendly PubSub Action target.
    /// </summary>
    public sealed record PubSubActionTargetInfo(
        string ConnectionName,
        ushort DataSetWriterId,
        ushort ActionTargetId,
        string ActionName,
        string DataSetWriterName,
        string PublishedDataSetName,
        string Description,
        string Source);

    /// <summary>
    /// A JSON-friendly PubSub Action responder registration.
    /// </summary>
    public sealed record PubSubActionResponderRegistration(
        string ConnectionName,
        ushort DataSetWriterId,
        ushort ActionTargetId,
        string ActionName,
        string ResponderKind,
        string Details)
    {
        /// <summary>
        /// Gets whether the responder target matches a target info.
        /// </summary>
        public bool MatchesTarget(PubSubActionTargetInfo target)
        {
            return string.Equals(ConnectionName, target.ConnectionName, StringComparison.Ordinal) &&
                DataSetWriterId == target.DataSetWriterId &&
                ActionTargetId == target.ActionTargetId &&
                string.Equals(ActionName, target.ActionName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets whether the responder target matches another responder registration.
        /// </summary>
        public bool MatchesTarget(PubSubActionResponderRegistration target)
        {
            return string.Equals(ConnectionName, target.ConnectionName, StringComparison.Ordinal) &&
                DataSetWriterId == target.DataSetWriterId &&
                ActionTargetId == target.ActionTargetId &&
                string.Equals(ActionName, target.ActionName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Converts the registration to a target info entry.
        /// </summary>
        public PubSubActionTargetInfo ToTargetInfo(
            string source,
            string dataSetWriterName,
            string publishedDataSetName)
        {
            return new PubSubActionTargetInfo(
                ConnectionName,
                DataSetWriterId,
                ActionTargetId,
                ActionName,
                dataSetWriterName,
                publishedDataSetName,
                Details,
                source);
        }
    }

    internal static partial class PubSubRuntimeManagerLog
    {
        [LoggerMessage(EventId = McpPubSubEventIds.PubSubRuntimeManager + 0, Level = LogLevel.Warning,
            Message = "Stopping the PubSub runtime failed.")]
        public static partial void StoppingPubSubRuntimeFailed(this ILogger logger, Exception exception);
    }
}
