// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// An opt-in observer that receives the raw, wire-level bytes a PubSub
    /// transport sends or receives. Implementations are typically external
    /// diagnostic taps (packet capture / dissection tooling) that store the
    /// bytes so the PubSub traffic can be reconstructed and decoded
    /// offline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The observer is invoked synchronously on the transport send /
    /// receive path. Implementations MUST be fast and non-throwing -
    /// exceptions thrown by an observer are swallowed by the transport, but
    /// observers should not rely on that behaviour. Heavy work (disk I/O,
    /// formatting) must be deferred to a background queue.
    /// </para>
    /// <para>
    /// The bytes passed to the observer are the exact wire bytes, including
    /// any PubSub message-level security (UADP encryption / signing). An
    /// offline dissector recovers the cleartext by resolving the
    /// <c>SecurityTokenId</c> in the UADP SecurityHeader to the matching
    /// security key (captured key log or live SKS) - see Part 14 §8.3.
    /// </para>
    /// <para>
    /// The interface lives in <c>Opc.Ua.PubSub</c> alongside the transport
    /// abstraction; the transports do NOT take a direct dependency on a
    /// capture implementation. A consumer wires the observer into the
    /// pipeline by registering it with the
    /// <see cref="IPubSubCaptureRegistry"/> (for example via the
    /// <c>OPCFoundation.NetStandard.Opc.Ua.PubSub.Diagnostics</c> package) -
    /// when no observer is registered there is no runtime cost on the
    /// transport's send / receive path beyond a single volatile read.
    /// </para>
    /// </remarks>
    public interface IPubSubCaptureObserver
    {
        /// <summary>
        /// Called when a transport is about to send, or has just received,
        /// a wire-level frame.
        /// </summary>
        /// <param name="context">
        /// Non-payload metadata describing the frame (direction, transport
        /// profile, endpoint / topic, timestamp).
        /// </param>
        /// <param name="payload">
        /// The frame bytes. The buffer is only valid for the duration of
        /// the call - copy it if it must outlive the invocation.
        /// </param>
        void OnFrameCaptured(in PubSubCaptureContext context, ReadOnlySpan<byte> payload);
    }
}
