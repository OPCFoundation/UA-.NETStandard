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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Transcoding;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// One owned, bounded route over the shipped transcoder/bridge, not an expression or script engine.
    /// </summary>
    internal sealed class McpPubSubBridge : IAsyncDisposable
    {
        /// <summary>
        /// Creates a stopped route with two privately owned application resources.
        /// </summary>
        internal McpPubSubBridge(
            PubSubBridgeInput input, McpPubSubRuntimeManager runtimes, McpPubSubOptions options,
            ITelemetryContext telemetry, TimeProvider time)
        {
            Validate(input, options);
            m_input = input;
            m_options = options;
            m_telemetry = telemetry;
            m_time = time;
            m_source = runtimes.CreateRuntime(input.Source);
            m_target = runtimes.CreateRuntime(input.Target, egressOnly: true);
        }

        /// <summary>
        /// Validates the complete declarative route without activating transports.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        internal static void Validate(PubSubBridgeInput input, McpPubSubOptions options)
        {
            ArgumentNullException.ThrowIfNull(input);
            PubSubInputValidation.Text(input.Name, 128);
            PubSubInputValidation.Validate(input.Source, options);
            PubSubInputValidation.Validate(input.Target, options);
            if (input.Source.Mode != "Subscriber" ||
                input.Target.Mode != "Publisher" ||
                (input.SelectFields.Count > 0 && input.ExcludeFields.Count > 0))
            {
                throw new ArgumentException(
                    "A route requires a subscriber source, publisher target and one projection mode.");
            }
            if (input.AllowSecurityDowngrade && !options.AllowSecurityDowngrade)
            {
                throw McpPubSubTransportProvider.Refused();
            }
            if (input.Source.Security is not null &&
                (input.Target.Security is null ||
                    (input.Source.Security.Mode == MessageSecurityMode.SignAndEncrypt &&
                        input.Target.Security.Mode != MessageSecurityMode.SignAndEncrypt)) &&
                !input.AllowSecurityDowngrade)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityModeRejected,
                    "The route would lower source message security; independent host and caller consent are required.");
            }
            if (input.PublisherId is { } publisher)
            {
                _ = PubSubInputValidation.Publisher(publisher, input.Target.Connection.Encoding);
            }
            ArgumentOutOfRangeException.ThrowIfGreaterThan(input.RenameFields.Count, options.MaxFields);
            var names = input.Source.Fields.ToList().Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
            var renamed = new HashSet<string>(StringComparer.Ordinal);
            foreach (PubSubFieldRenameInput rename in input.RenameFields)
            {
                PubSubInputValidation.Text(rename.Target, 128);
                if (!names.Remove(rename.Source) || !renamed.Add(rename.Target) || names.Contains(rename.Target))
                {
                    throw new ArgumentException("Renames require known unique sources and non-colliding targets.");
                }
            }
            names.UnionWith(renamed);
            foreach (ArrayOf<string> selection in new[]
            {
                input.SelectFields, input.ExcludeFields, input.PromoteFields
            })
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThan(selection.Count, options.MaxFields);
                var unique = new HashSet<string>(StringComparer.Ordinal);
                foreach (string field in selection)
                {
                    if (!names.Contains(field) || !unique.Add(field))
                    {
                        throw new ArgumentException(
                            "Field transforms require unique names in the declared output schema.");
                    }
                }
            }
        }

        /// <summary>
        /// Starts both approved connections and then registers the existing transcoding bridge.
        /// </summary>
        internal async ValueTask<JsonObject> StartAsync(CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                if (m_bridge is not null)
                {
                    return await StatusCoreAsync(ct).ConfigureAwait(false);
                }
                try
                {
                    _ = await m_target.StartAsync(ct).ConfigureAwait(false);
                    _ = await m_source.StartAsync(ct).ConfigureAwait(false);
                    PubSubTranscoderBuilder builder = Specification(m_input);
                    IPubSubApplication application = m_source.Application();
                    var messageContext = new PubSubNetworkMessageContext(
                        ServiceMessageContext.CreateEmpty(m_telemetry),
                        application.MetaDataRegistry, application.Diagnostics, m_time);
                    var context = new TranscodeContext(messageContext, m_telemetry);
                    PubSubSecurityContext? targetSecurity = m_target.SecurityResolver?.Resolve(
                        m_target.Connection().Configuration);
                    var security = new TranscodeSecurity
                    {
                        TargetWrapper = targetSecurity?.Wrapper,
                        TargetWrapOptions = targetSecurity?.WrapOptions ?? UadpSecurityWrapOptions.SignAndEncrypt,
                        AllowInsecureCrossEncoding = m_input.AllowSecurityDowngrade
                    };
                    var uadp = new UadpEncoder();
                    var json = new PubSub.Encoding.Json.JsonEncoder();
                    var encoders = new Dictionary<string, INetworkMessageEncoder>(StringComparer.Ordinal)
                    {
                        [uadp.TransportProfileUri] = uadp,
                        [json.TransportProfileUri] = json
                    };
                    var transcoder = new PubSubTranscoder(builder.BuildSpec(), encoders, context, security);
                    m_egress = new BoundedEgress(new ConnectionTranscodeEgress(m_target.Connection()), m_options);
                    m_bridge = new PubSubTranscodingBridge(m_source.Connection(), transcoder, m_egress,
                        m_telemetry, _ => m_input.Target.Connection.Topic);
                    m_bridge.Start();
                    return await StatusCoreAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    await StopCoreAsync().ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Detaches forwarding before draining both owned application transports.
        /// </summary>
        internal async ValueTask<JsonObject> StopAsync(CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await StopCoreAsync().ConfigureAwait(false);
                return await StatusCoreAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Reports route state and actual source/egress diagnostics without starting either endpoint.
        /// </summary>
        internal async ValueTask<JsonObject> StatusAsync(CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await StatusCoreAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await m_gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (m_disposed)
                {
                    return;
                }
                await StopCoreAsync().ConfigureAwait(false);
                await m_source.DisposeAsync().ConfigureAwait(false);
                await m_target.DisposeAsync().ConfigureAwait(false);
                m_disposed = true;
            }
            finally
            {
                m_gate.Release();
            }
            m_gate.Dispose();
        }

        /// <summary>
        /// Reuses only supported declarative transform primitives.
        /// </summary>
        internal static PubSubTranscoderBuilder Specification(PubSubBridgeInput input)
        {
            PubSubTranscoderBuilder builder = new PubSubTranscoderBuilder()
                .From(McpPubSubConfiguration.ConnectionName)
                .To(McpPubSubConfiguration.ConnectionName,
                    input.Target.Connection.Encoding == PubSubMessageEncoding.Json
                        ? TranscodeEncoding.Json : TranscodeEncoding.Uadp)
                .PreserveMetaDataVersion(input.PreserveMetadataVersion);
            if (input.PublisherId is not null || input.WriterGroupId is not null)
            {
                builder.RemapIds(input.PublisherId is { } publisher
                    ? PubSubInputValidation.Publisher(publisher, input.Target.Connection.Encoding) : PublisherId.Null,
                    input.WriterGroupId);
            }
            foreach (PubSubFieldRenameInput rename in input.RenameFields)
            {
                builder.RenameField(rename.Source, rename.Target);
            }
            if (input.SelectFields.Count > 0)
            {
                builder.SelectFields(input.SelectFields.ToArray() ?? []);
            }
            if (input.ExcludeFields.Count > 0)
            {
                builder.ExcludeFields(input.ExcludeFields.ToArray() ?? []);
            }
            if (input.PromoteFields.Count > 0)
            {
                builder.PromoteFields(input.PromoteFields.ToArray() ?? []);
            }
            if (input.DropKeepAlive)
            {
                builder.DropKeepAlive();
            }
            return builder;
        }

        /// <summary>
        /// Unregisters first, drains source callbacks second and closes the egress last.
        /// </summary>
        private async ValueTask StopCoreAsync()
        {
            if (m_bridge is not null)
            {
                await m_bridge.DisposeAsync().ConfigureAwait(false);
                m_bridge = null;
            }
            try
            {
                _ = await m_source.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _ = await m_target.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Projects only safe state and counters.
        /// </summary>
        private async ValueTask<JsonObject> StatusCoreAsync(CancellationToken ct)
        {
            return new JsonObject
            {
                ["name"] = m_input.Name,
                ["running"] = m_bridge is not null,
                ["source"] = await m_source.StatusAsync(ct).ConfigureAwait(false),
                ["target"] = await m_target.StatusAsync(ct).ConfigureAwait(false),
                ["forwardedMessages"] = m_egress?.Forwarded ?? 0,
                ["oversizedDropped"] = m_egress?.Oversized ?? 0,
                ["securityDowngradeAllowed"] = m_input.AllowSecurityDowngrade,
                ["targetCyclicPublishing"] = false
            };
        }

        private readonly PubSubBridgeInput m_input;
        private readonly McpPubSubOptions m_options;
        private readonly ITelemetryContext m_telemetry;
        private readonly TimeProvider m_time;
        private readonly McpPubSubRuntime m_source;
        private readonly McpPubSubRuntime m_target;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private PubSubTranscodingBridge? m_bridge;
        private BoundedEgress? m_egress;
        private bool m_disposed;

        /// <summary>
        /// Applies output frame/count/aggregate budgets before the existing egress sends any part of a result.
        /// </summary>
        private sealed class BoundedEgress(IPubSubTranscodeEgress inner, McpPubSubOptions options)
            : IPubSubTranscodeEgress
        {
            /// <inheritdoc/>
            public string TransportProfileUri => inner.TransportProfileUri;

            /// <summary>Gets successfully forwarded messages.</summary>
            internal long Forwarded => Interlocked.Read(ref m_forwarded);

            /// <summary>Gets messages rejected before any frame was sent.</summary>
            internal long Oversized => Interlocked.Read(ref m_oversized);

            /// <inheritdoc/>
            public async ValueTask SendAsync(
                TranscodeResult result, string? topic = null, CancellationToken cancellationToken = default)
            {
                if (result.Frames.Count > options.MaxBufferedMessages ||
                    result.Frames.ToList().Any(frame => frame.Length > options.MaxMessageBytes) ||
                    result.Frames.ToList().Sum(frame => (long)frame.Length) > options.MaxBufferedBytes ||
                    result.Properties.Count > options.MaxFields)
                {
                    Interlocked.Increment(ref m_oversized);
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "The transcode result exceeds the host's frame/count/byte ceilings.");
                }
                await inner.SendAsync(result, topic, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref m_forwarded);
            }

            private long m_forwarded;
            private long m_oversized;
        }
    }
}
