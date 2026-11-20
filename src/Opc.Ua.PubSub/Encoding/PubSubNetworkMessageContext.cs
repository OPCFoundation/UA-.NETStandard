// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Environment passed to every PubSub encode / decode invocation.
    /// Bundles the per-message dependencies (stack message context,
    /// metadata registry, diagnostics sink, clock) so encoder /
    /// decoder implementations do not need to acquire them from
    /// ambient state.
    /// </summary>
    /// <remarks>
    /// Implements the encode/decode environment expected by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4">
    /// Part 14 §7.2.4 UADP NetworkMessage mapping</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5">
    /// Part 14 §7.2.5 JSON NetworkMessage mapping</see>. Holds no
    /// per-component state; one instance can safely be shared across
    /// every encode / decode on the same publisher / subscriber.
    /// </remarks>
    public sealed class PubSubNetworkMessageContext
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubNetworkMessageContext"/>.
        /// </summary>
        /// <param name="messageContext">
        /// Stack-level message context used by primitive
        /// encoders / decoders.
        /// </param>
        /// <param name="metaDataRegistry">
        /// Registry used to resolve <see cref="DataSetMetaDataType"/>
        /// for decoding RawData / Variant payloads.
        /// </param>
        /// <param name="diagnostics">
        /// Diagnostics sink for per-message counters and last-error
        /// recording.
        /// </param>
        /// <param name="timeProvider">
        /// Clock used to stamp received frames and to detect
        /// chunk-reassembly timeouts.
        /// </param>
        /// <param name="uadpActionFieldEncoding">
        /// Configured UADP Action payload field encoding used when
        /// decoding Action messages.
        /// </param>
        public PubSubNetworkMessageContext(
            IServiceMessageContext messageContext,
            IDataSetMetaDataRegistry metaDataRegistry,
            IPubSubDiagnostics diagnostics,
            TimeProvider timeProvider,
            PubSubFieldEncoding uadpActionFieldEncoding = PubSubFieldEncoding.Variant)
        {
            if (messageContext is null)
            {
                throw new ArgumentNullException(nameof(messageContext));
            }
            if (metaDataRegistry is null)
            {
                throw new ArgumentNullException(nameof(metaDataRegistry));
            }
            if (diagnostics is null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            MessageContext = messageContext;
            MetaDataRegistry = metaDataRegistry;
            Diagnostics = diagnostics;
            TimeProvider = timeProvider;
            UadpActionFieldEncoding = uadpActionFieldEncoding;
        }

        /// <summary>
        /// Stack-level encoding context (namespace table, server uris,
        /// max array length, etc.) used by primitive readers and
        /// writers.
        /// </summary>
        public IServiceMessageContext MessageContext { get; }

        /// <summary>
        /// Shared <see cref="IDataSetMetaDataRegistry"/> used by the
        /// decoder to rehydrate Variant and RawData payloads.
        /// </summary>
        public IDataSetMetaDataRegistry MetaDataRegistry { get; }

        /// <summary>
        /// Diagnostics sink for per-message counters.
        /// </summary>
        public IPubSubDiagnostics Diagnostics { get; }

        /// <summary>
        /// Clock used to stamp inbound frames and to detect chunk
        /// reassembly timeouts.
        /// </summary>
        public TimeProvider TimeProvider { get; }

        /// <summary>
        /// Configured UADP Action DataSetMessage field encoding. Part 14
        /// §7.2.4.5.9 and §7.2.4.5.10 allow Action request and response
        /// fields to use Variant or RawData encoding.
        /// </summary>
        public PubSubFieldEncoding UadpActionFieldEncoding { get; }
    }
}
