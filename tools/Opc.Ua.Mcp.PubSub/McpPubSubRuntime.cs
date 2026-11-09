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
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Owns exactly one initially stopped application, its bounded data buffers and private credential copies.
    /// </summary>
    internal sealed class McpPubSubRuntime : IAsyncDisposable
    {
        /// <summary>
        /// Builds only in-memory schema/source state; no factory or network is activated.
        /// </summary>
        internal McpPubSubRuntime(
            PubSubApplicationInput input, McpPubSubOptions options,
            IMcpPubSubTransportProvider transports, McpSecretReferences secrets,
            ITelemetryContext telemetry, TimeProvider time, McpPubSubKeyReferences keyReferences,
            bool egressOnly = false)
        {
            Input = input;
            m_options = options;
            m_transports = transports;
            m_secrets = secrets;
            m_telemetry = telemetry;
            m_time = time;
            m_keyReferences = keyReferences;
            m_egressOnly = egressOnly;
            m_context = ServiceMessageContext.CreateEmpty(telemetry);
            m_source = new McpPubSubDataSetSource(input, options, m_context, time);
            m_buffer = new McpPubSubDataSetBuffer(options, m_context, time);
            m_publishRate = new McpPubSubRateBudget(options.MaxMessagesPerSecond, time);
        }

        /// <summary>
        /// Gets the immutable validated input. Do not serialize its credential references as a configuration artifact.
        /// </summary>
        internal PubSubApplicationInput Input { get; }

        /// <summary>
        /// Gets the explicit message-security resolver used by a bridge, without exposing key material.
        /// </summary>
        internal IPubSubSecurityWrapperResolver? SecurityResolver { get; private set; }

        /// <summary>
        /// Starts an explicitly authorized backend once and never replaces another runtime.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask<JsonObject> StartAsync(CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                if (m_bindingInvalidated ||
                    m_bindings.OfType<McpPubSubBorrowedSession>().Any(binding => binding.Invalidated.IsCompleted))
                {
                    throw new ServiceResultException(StatusCodes.BadSessionIdInvalid,
                        "The bound session generation is no longer current; create a new explicit binding.");
                }
                if (m_application is not null)
                {
                    return StatusCore();
                }
                IPubSubApplication? candidate = null;
                try
                {
                    if (Input.Connection.CredentialReference is { } credential)
                    {
                        using ISecret secret = await m_secrets.ResolveAsync(
                            credential, Input.Connection.Endpoint, McpCapability.PubSubRuntime, ct)
                            .ConfigureAwait(false);
                        m_credentials = new RuntimeCredentials(secret.Bytes);
                    }
                    IPubSubTransportFactory factory = m_transports.CreateFactory(Input, m_credentials);
                    PubSubConfigurationDataType configuration = McpPubSubConfiguration.Build(
                        Input, m_source.BuildMetaData());
                    ArrayOf<EndpointDescription> keyServices = Input.Security is { } secured
                        ? [m_keyReferences.KeyService(secured)] : [];
                    foreach (WriterGroupDataType group in configuration.Connections[0].WriterGroups)
                    {
                        group.MaxNetworkMessageSize = (uint)Math.Min(65507, m_options.MaxMessageBytes);
                        group.Enabled = !m_egressOnly;
                        group.SecurityKeyServices = keyServices;
                    }
                    foreach (ReaderGroupDataType group in configuration.Connections[0].ReaderGroups)
                    {
                        group.MaxNetworkMessageSize = (uint)Math.Min(65507, m_options.MaxMessageBytes);
                        group.SecurityKeyServices = keyServices;
                        foreach (DataSetReaderDataType reader in group.DataSetReaders)
                        {
                            reader.SecurityKeyServices = keyServices;
                        }
                    }
                    PubSubApplicationBuilder builder = new PubSubApplicationBuilder(m_telemetry)
                        .WithApplicationId("urn:opcfoundation:mcp:pubsub:" + Guid.NewGuid().ToString("N"))
                        .WithTimeProvider(m_time)
                        .AddTransportFactory(factory)
                        .AddEncoder(new UadpEncoder())
                        .AddDecoder(new UadpDecoder())
                        .AddEncoder(new PubSub.Encoding.Json.JsonEncoder())
                        .AddDecoder(new PubSub.Encoding.Json.JsonDecoder())
                        .WithDiagnosticsLevel(PubSubDiagnosticsLevel.Medium)
                        .AddDataSetSource(Input.DataSetName, m_boundSource ?? m_source)
                        .AddSubscribedDataSetSink(McpPubSubConfiguration.ReaderName, m_boundSink ?? m_buffer)
                        .UseConfiguration(configuration);
                    await ConfigureSecurityAsync(builder, ct).ConfigureAwait(false);
                    candidate = builder.Build();
                    if (m_action is { } action)
                    {
                        candidate.RegisterActionHandler(action.Target, action.Handler, action.AllowUnsecured);
                    }
                    await candidate.StartAsync(ct).ConfigureAwait(false);
                    m_application = candidate;
                    candidate = null;
                    m_startFailure = false;
                    return StatusCore();
                }
                catch
                {
                    m_startFailure = true;
                    throw;
                }
                finally
                {
                    if (candidate is not null)
                    {
                        await candidate.DisposeAsync().ConfigureAwait(false);
                    }
                    if (m_application is null)
                    {
                        ClearCredentials();
                    }
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Stops transport work but retains the named lease, declared schema and buffered records.
        /// </summary>
        internal async ValueTask<JsonObject> StopAsync(CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await StopCoreAsync().ConfigureAwait(false);
                return StatusCore();
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Applies a bounded source update without claiming transport delivery.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask<JsonObject> PublishAsync(PubSubPublishInput input, CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                if (m_application is null || Input.Mode != "Publisher" || m_boundSource is not null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "Publishing requires a running publisher without an external source binding.");
                }
                if (!m_publishRate.TryTake())
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations,
                        "The host's sample-update rate budget is exhausted.");
                }
                return m_source.Update(input);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Reads an existing subscriber buffer without starting or implying an active transport.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask<JsonObject> ReadAsync(int maximum, bool clear, CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                if (Input.Mode != "Subscriber")
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "This runtime is not a subscriber.");
                }
                JsonObject result = m_buffer.Read(maximum, clear);
                result["running"] = m_application is not null;
                result["externalSink"] = m_boundSink is not null;
                return result;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Snapshots runtime state and genuine SDK diagnostics without starting a transport.
        /// </summary>
        internal async ValueTask<JsonObject> StatusAsync(CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return StatusCore();
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Attaches one explicitly authorized source or sink only while the application is stopped.
        /// Ownership of the binding is transferred only after successful attachment.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask BindAsync(
            IPublishedDataSetSource? source, ISubscribedDataSetSink? sink,
            IAsyncDisposable binding, CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                if (m_application is not null ||
                    m_bindings.Count != 0 ||
                    (source is not null && Input.Mode != "Publisher") ||
                    (sink is not null && Input.Mode != "Subscriber"))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "Bind one source/sink to a stopped runtime of the matching mode.");
                }
                m_boundSource = source;
                m_boundSink = sink;
                m_bindings.Add(binding);
                ObserveBinding(binding);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Attaches one explicitly authorized method responder to a stopped runtime.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask BindActionAsync(
            PubSubActionTarget target, IPubSubActionHandler handler, IAsyncDisposable binding,
            bool allowUnsecured, CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                if (m_application is not null || m_bindings.Count != 0)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "Attach one action responder to a stopped, unbound runtime.");
                }
                m_action = new BoundAction(target, handler, allowUnsecured);
                m_bindings.Add(binding);
                ObserveBinding(binding);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Returns a fresh copy of the explicit immutable metadata for adapter configuration.
        /// </summary>
        internal DataSetMetaDataType DeclaredMetadata()
        {
            return m_source.BuildMetaData();
        }

        /// <summary>
        /// Gives a bridge its owned application's connection after explicit activation.
        /// The bridge owns this runtime and stops forwarding before disposing it.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal IPubSubConnection Connection()
        {
            return m_application?.Connections.Single() ??
                throw new ServiceResultException(StatusCodes.BadInvalidState, "Start the owned runtime first.");
        }

        /// <summary>
        /// Gets the current application's typed context for the existing transcoder.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal IPubSubApplication Application()
        {
            return m_application ?? throw new ServiceResultException(StatusCodes.BadInvalidState);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Task? observer;
            await m_gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposalSignal.TrySetResult();
                await StopCoreAsync().ConfigureAwait(false);
                foreach (IAsyncDisposable binding in m_bindings)
                {
                    await binding.DisposeAsync().ConfigureAwait(false);
                }
                m_bindings.Clear();
                m_boundSource = null;
                m_boundSink = null;
                m_action = null;
                m_disposed = true;
                observer = m_bindingObserver;
            }
            finally
            {
                m_gate.Release();
            }
            if (observer is not null)
            {
                await observer.ConfigureAwait(false);
            }
            m_gate.Dispose();
        }

        /// <summary>
        /// Observes the lifetime of an owned adapter without keeping a revoked request context authoritative.
        /// </summary>
        private void ObserveBinding(IAsyncDisposable binding)
        {
            if (binding is McpPubSubBorrowedSession session)
            {
                m_bindingObserver = ObserveBindingAsync(session);
            }
        }

        /// <summary>
        /// Stops transport work after a captured session/model generation is invalidated.
        /// The named lease remains available for status/removal and is not silently rebound.
        /// </summary>
        private async Task ObserveBindingAsync(McpPubSubBorrowedSession binding)
        {
            _ = await Task.WhenAny(binding.Invalidated, m_disposalSignal.Task).ConfigureAwait(false);
            await m_gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (m_disposed || m_disposalSignal.Task.IsCompleted)
                {
                    return;
                }
                m_bindingInvalidated = true;
                await StopCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Loads concatenated SKS material only into the existing key-ring/provider implementation.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask ConfigureSecurityAsync(PubSubApplicationBuilder builder, CancellationToken ct)
        {
            if (Input.Security is not { } security)
            {
                return;
            }
            McpSecretReference metadata = m_secrets.RequireReference(
                security.KeyReference, security.Resource, McpCapability.PubSubSecurity);
            if (security.ExpiresAt > metadata.ExpiresAt || security.ExpiresAt <= m_time.GetUtcNow())
            {
                throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected, "The SKS key lease is invalid.");
            }
            using ISecret secret = await m_secrets.ResolveAsync(
                security.KeyReference, security.Resource, McpCapability.PubSubSecurity, ct).ConfigureAwait(false);
            IPubSubSecurityPolicy policy = security.PolicyUri == PubSubSecurityPolicyUri.PubSubAes128Ctr
                ? new PubSubAes128CtrPolicy() : new PubSubAes256CtrPolicy();
            int encryptingLength = policy.EncryptingKeyLength;
            int signingLength = policy.SigningKeyLength;
            const int nonceLength = 4;
            if (secret.Bytes.Length != signingLength + encryptingLength + nonceLength)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed,
                    "The stored SKS key size is invalid.");
            }
            m_keys = new PubSubSecurityKeyRing(security.GroupId, m_time, pastKeyLimit: 0);
            m_activeKey = new PubSubSecurityKey(security.TokenId,
                ByteString.From(secret.Bytes[..signingLength].ToArray()),
                ByteString.From(secret.Bytes.Slice(signingLength, encryptingLength).ToArray()),
                ByteString.From(secret.Bytes.Slice(signingLength + encryptingLength, nonceLength).ToArray()),
                DateTimeUtc.From(m_time.GetUtcNow()), security.ExpiresAt - m_time.GetUtcNow());
            m_keys.SetCurrent(m_activeKey);
            var provider = new StaticSecurityKeyProvider(security.GroupId, m_keys);
            builder.AddSecurityKeyProvider(provider).WithSecurityPolicySelector((_, _) => policy);
            SecurityResolver = new PubSubSecurityWrapperResolver(
                [provider], m_telemetry, m_time, policySelector: (_, _) => policy);
        }

        /// <summary>
        /// Drains transport callbacks before zeroizing credentials and keys.
        /// </summary>
        private async ValueTask StopCoreAsync()
        {
            if (m_application is not null)
            {
                await m_application.DisposeAsync().ConfigureAwait(false);
                m_application = null;
            }
            ClearCredentials();
        }

        /// <summary>
        /// Clears per-runtime secret copies without disposing the host secret store.
        /// </summary>
        private void ClearCredentials()
        {
            m_credentials?.Dispose();
            m_credentials = null;
            m_keys?.Dispose();
            m_keys = null;
            m_activeKey?.Dispose();
            m_activeKey = null;
            SecurityResolver = null;
        }

        /// <summary>
        /// Keeps output free of user credentials, key bytes and source configuration internals.
        /// </summary>
        private JsonObject StatusCore()
        {
            var counters = new JsonObject();
            if (m_application is { } application)
            {
                foreach (PubSubDiagnosticsCounterKind kind in Enum.GetValues<PubSubDiagnosticsCounterKind>())
                {
                    counters[kind.ToString()] = application.Diagnostics.Read(kind);
                }
            }
            var bindings = new JsonArray();
            foreach (McpPubSubBorrowedSession binding in m_bindings.OfType<McpPubSubBorrowedSession>())
            {
                bindings.Add((JsonNode)binding.Status());
            }
            return new JsonObject
            {
                ["name"] = Input.Name,
                ["mode"] = Input.Mode,
                ["transport"] = Input.Connection.Transport.ToString(),
                ["encoding"] = Input.Connection.Encoding.ToString(),
                ["endpoint"] = Input.Connection.Endpoint,
                ["isRunning"] = m_application is not null,
                ["state"] = m_disposed ? "Removed" : m_bindingInvalidated ? "BindingInvalidated" :
                    m_application?.State.State.ToString() ??
                    (m_startFailure ? "StartFailed" : "Stopped"),
                ["publisherId"] = McpCompanionJson.Variant(
                    PubSubInputValidation.Publisher(Input.PublisherId, Input.Connection.Encoding).ToVariant(),
                    m_context),
                ["writerGroupId"] = Input.WriterGroupId,
                ["dataSetWriterId"] = Input.DataSetWriterId,
                ["metadata"] = McpCompanionJson.Encode(m_source.BuildMetaData(), m_context),
                ["buffer"] = m_buffer.Status(),
                ["diagnostics"] = counters,
                ["configurationVersion"] = m_application is null ? null :
                    McpCompanionJson.Encode(m_application.ConfigurationVersion, m_context),
                ["credentialLifecycle"] = Input.Connection.CredentialReference?.StartsWith("alias:",
                    StringComparison.Ordinal) == true
                    ? "Host alias resolved at each start; no alias revocation event; bounded by runtime lease"
                    : "Issued-reference expiry/invalidation closes the lease",
                ["bindingCount"] = m_bindings.Count,
                ["bindings"] = bindings
            };
        }

        private readonly McpPubSubOptions m_options;
        private readonly IMcpPubSubTransportProvider m_transports;
        private readonly McpSecretReferences m_secrets;
        private readonly ITelemetryContext m_telemetry;
        private readonly TimeProvider m_time;
        private readonly McpPubSubKeyReferences m_keyReferences;
        private readonly bool m_egressOnly;
        private readonly IServiceMessageContext m_context;
        private readonly McpPubSubDataSetSource m_source;
        private readonly McpPubSubDataSetBuffer m_buffer;
        private readonly McpPubSubRateBudget m_publishRate;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly List<IAsyncDisposable> m_bindings = [];

        private readonly TaskCompletionSource m_disposalSignal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Task? m_bindingObserver;
        private IPublishedDataSetSource? m_boundSource;
        private ISubscribedDataSetSink? m_boundSink;
        private BoundAction? m_action;
        private IPubSubApplication? m_application;
        private RuntimeCredentials? m_credentials;
        private PubSubSecurityKeyRing? m_keys;
        private PubSubSecurityKey? m_activeKey;
        private bool m_startFailure;
        private bool m_disposed;
        private bool m_bindingInvalidated;

        /// <summary>
        /// Holds one preauthorized typed action handler.
        /// </summary>
        private sealed record BoundAction(
            PubSubActionTarget Target, IPubSubActionHandler Handler, bool AllowUnsecured);

        /// <summary>
        /// A single zeroized runtime credential view used by the existing synchronous factory lookup.
        /// The protected host store was already resolved asynchronously before this view was created.
        /// </summary>
        private sealed class RuntimeCredentials : ISecretRegistry, IDisposable
        {
            /// <summary>
            /// Copies only the already-authorized password bytes.
            /// </summary>
            internal RuntimeCredentials(ReadOnlySpan<byte> bytes)
            {
                m_bytes = bytes.ToArray();
            }

            /// <inheritdoc/>
            public void RegisterStore(ISecretStore store)
            {
                throw new NotSupportedException("A runtime credential view cannot register stores.");
            }

            /// <inheritdoc/>
            public ISecret? TryGet(SecretIdentifier id)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                return id.StoreType == "McpRuntime" && id.Name == "password"
                    ? new SecretView(m_bytes) : null;
            }

            /// <inheritdoc/>
            public async ValueTask<ISecret?> GetAsync(SecretIdentifier id, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                ISecret? secret = null;
                try
                {
                    secret = TryGet(id);
                    ISecret? result = await ValueTask.FromResult(secret).ConfigureAwait(false);
                    secret = null;
                    return result;
                }
                finally
                {
                    secret?.Dispose();
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                CryptographicOperations.ZeroMemory(m_bytes);
                m_disposed = true;
            }

            private readonly byte[] m_bytes;
            private bool m_disposed;

            /// <summary>
            /// Owns a short-lived copy consumed by one MQTT connection factory.
            /// </summary>
            private sealed class SecretView(ReadOnlySpan<byte> bytes) : ISecret
            {
                /// <inheritdoc/>
                public ReadOnlySpan<byte> Bytes => m_copy;

                /// <inheritdoc/>
                public void Dispose()
                {
                    CryptographicOperations.ZeroMemory(m_copy);
                }

                private readonly byte[] m_copy = bytes.ToArray();
            }
        }
    }
}
