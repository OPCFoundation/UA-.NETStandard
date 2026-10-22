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
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.OpenUsd.Client;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// A selected primary-session stage. Reuses the bounded V2 source and connector conversion,
    /// never the connector's command path or a second client/session.
    /// </summary>
    internal sealed class OpenUsdViewportSource : IAsyncDisposable
    {
        /// <summary>
        /// Captures source ownership and immutable metadata before any native resources exist.
        /// </summary>
        public OpenUsdViewportSource(
            CompanionContext context, CompanionTarget target, CompanionOperationDraft evidence,
            ByteString metadataDigest, string rootIdentifier, string primPath,
            ArrayOf<OpenUsdConnector.BindingInfo> bindings, ArrayOf<OpenUsdVerifiedAsset> assets,
            IOpenUsdCompanionReader reader, IOpenUsdWorkflowSource changes)
        {
            m_context = context;
            m_target = target;
            m_evidence = evidence;
            m_metadataDigest = metadataDigest.Copy();
            m_bindings = bindings.ConvertAll(static binding => new OpenUsdConnector.BindingInfo
            {
                SourceNodeId = binding.SourceNodeId,
                PrimPath = binding.PrimPath,
                PropertyName = binding.PropertyName,
                Kind = binding.Kind,
                Scale = binding.Scale,
                Offset = binding.Offset,
                Intent = binding.Intent,
                Enabled = binding.Enabled,
                BindingDefinitionId = binding.BindingDefinitionId,
                SourceEngineeringUnits = CopyUnits(binding.SourceEngineeringUnits),
                TargetEngineeringUnits = CopyUnits(binding.TargetEngineeringUnits)
            });
            m_reader = reader;
            m_changes = changes;
            RootIdentifier = rootIdentifier;
            PrimPath = primPath;
            Assets = assets;
        }

        /// <summary>
        /// Gets the logical root asset identity, not a remote URI to fetch.
        /// </summary>
        public string RootIdentifier { get; }

        /// <summary>
        /// Gets the represented prim in the captured stage.
        /// </summary>
        public string PrimPath { get; }

        /// <summary>
        /// Gets the already bounded and digest-verified closure.
        /// </summary>
        public ArrayOf<OpenUsdVerifiedAsset> Assets { get; }

        /// <summary>
        /// Gets the number of telemetry values authored into the retained stage.
        /// </summary>
        public long AppliedValues => Interlocked.Read(ref m_applied);

        /// <summary>
        /// Rejects source work after the borrowed primary session changes identity or generation.
        /// </summary>
        public void RequireCurrent()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            if (!m_context.Session.Connected || !m_evidence.MatchesSession(m_context.Session))
            {
                throw new InvalidOperationException("The viewport's primary session changed. Start again.");
            }
        }

        /// <summary>
        /// Streams only reviewed telemetry into the existing scheduler sink and monitors metadata changes.
        /// All subscription work is canceled and drained before this method completes.
        /// </summary>
        public async Task RunAsync(IUsdSink sink, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(sink);
            RequireCurrent();
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            for (int index = 0; index < m_bindings.Count; index++)
            {
                OpenUsdConnector.BindingInfo binding = m_bindings[index];
                DataValue value = await m_reader.ReadValueAsync(binding.SourceNodeId, lifetime.Token)
                    .ConfigureAwait(false);
                lifetime.Token.ThrowIfCancellationRequested();
                Apply(sink, binding, value);
            }
            Task observe = ObserveAsync(sink, lifetime.Token);
            Task metadata = MonitorMetadataAsync(lifetime.Token);
            try
            {
                Task completed = await Task.WhenAny(observe, metadata).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
                lifetime.Token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("The viewport observation ended before it was stopped.");
            }
            finally
            {
                await lifetime.CancelAsync().ConfigureAwait(false);
                try
                {
                    await Task.WhenAll(observe, metadata).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Preserve the failure that caused us to cancel the other branch.
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                await m_reader.DisposeAsync().ConfigureAwait(false);
            }
        }

        private async Task ObserveAsync(IUsdSink sink, CancellationToken cancellationToken)
        {
            if (m_bindings.Count == 0)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return;
            }
            ArrayOf<NodeId> nodes =
                [.. m_bindings.ToArray()!.Select(static binding => binding.SourceNodeId).Distinct()];
            await foreach (OpenUsdWorkflowChange change in m_changes.ObserveAsync(nodes, cancellationToken)
                .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequireCurrent();
                bool found = false;
                using (sink.BeginBatch())
                {
                    foreach (OpenUsdConnector.BindingInfo binding in m_bindings)
                    {
                        if (binding.SourceNodeId == change.Source)
                        {
                            found = true;
                            Apply(sink, binding, change.Value);
                        }
                    }
                }
                if (!found)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdInvalid,
                        "A live value did not belong to the reviewed OpenUSD bindings.");
                }
            }
        }

        private async Task MonitorMetadataAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                RequireCurrent();
                OpenUsdConnector.RepresentationInfo representation = await OpenUsdCompanionProvider.FindAsync(
                    m_reader, m_context, m_target.NodeId, cancellationToken).ConfigureAwait(false);
                ByteString current = OpenUsdWorkflowMetadata.Digest(m_context, representation);
                if (!CryptographicOperations.FixedTimeEquals(current.Span, m_metadataDigest.Span))
                {
                    throw new InvalidOperationException("Live OpenUSD binding or asset metadata changed. Start again.");
                }
            }
        }

        private void Apply(IUsdSink sink, OpenUsdConnector.BindingInfo binding, in DataValue value)
        {
            RequireCurrent();
            if (value.IsNull || !StatusCode.IsGood(value.StatusCode))
            {
                throw new ServiceResultException(value.IsNull ? StatusCodes.BadNoData : value.StatusCode,
                    "The live viewport received no good source value.");
            }
            Variant converted = OpenUsdConnector.Convert(binding, value.WrappedValue);
            OpenUsdWorkflowTasks.RequireConverted(converted);
            _ = OpenUsdWorkflowMetadata.EncodeSample(m_context, value, converted);
            sink.SetAttribute(binding.PrimPath!, binding.PropertyName!, converted);
            Interlocked.Increment(ref m_applied);
        }

        private static EUInformation? CopyUnits(EUInformation? units)
        {
            return units is null ? null : new EUInformation
            {
                NamespaceUri = units.NamespaceUri,
                UnitId = units.UnitId,
                DisplayName = units.DisplayName,
                Description = units.Description
            };
        }

        private readonly CompanionContext m_context;
        private readonly CompanionTarget m_target;
        private readonly CompanionOperationDraft m_evidence;
        private readonly ByteString m_metadataDigest;
        private readonly ArrayOf<OpenUsdConnector.BindingInfo> m_bindings;
        private readonly IOpenUsdCompanionReader m_reader;
        private readonly IOpenUsdWorkflowSource m_changes;
        private long m_applied;
        private int m_disposed;
    }
}
