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

namespace Opc.Ua.PubSub.Diagnostics
{
    /// <summary>
    /// Identifies one cumulative counter exposed by an
    /// <see cref="IPubSubDiagnostics"/> instance. Each value names a
    /// specific counter from the Part 14 PubSubDiagnosticsType node model
    /// (one row per <c>UADP</c> / <c>JSON</c> mapping or per state-machine
    /// transition reason).
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.11">
    /// Part 14 §9.1.11 PubSubDiagnosticsType</see>. The enum is exhaustive
    /// for the counters required to cover the
    /// implementation (state-transition cause counters, receive / send
    /// counters, security/decoder error counters, and chunking counters).
    /// </remarks>
    public enum PubSubDiagnosticsCounterKind
    {
        /// <summary>
        /// StateOperationalByMethod: cumulative count of times the
        /// component entered the Operational state because of a
        /// configuration method call (Enable / Resume).
        /// </summary>
        StateOperationalByMethod,

        /// <summary>
        /// StateOperationalByParent: cumulative count of times the
        /// component entered the Operational state because its parent
        /// cascaded an Enable / Resume to it.
        /// </summary>
        StateOperationalByParent,

        /// <summary>
        /// StateOperationalFromError: cumulative count of times the
        /// component recovered to Operational from the Error state
        /// (e.g. transport reconnect, security key refresh, valid
        /// DataSetMessage received after a receive-timeout).
        /// </summary>
        StateOperationalFromError,

        /// <summary>
        /// StatePausedByParent: cumulative count of times the component
        /// transitioned to Paused because its parent cascaded a Pause to
        /// it.
        /// </summary>
        StatePausedByParent,

        /// <summary>
        /// StateDisabledByMethod: cumulative count of times the component
        /// transitioned to Disabled because of an explicit Disable method
        /// call.
        /// </summary>
        StateDisabledByMethod,

        /// <summary>
        /// ReceivedNetworkMessages: cumulative count of NetworkMessages
        /// (UADP or JSON frames) received and parsed at this component.
        /// </summary>
        ReceivedNetworkMessages,

        /// <summary>
        /// ReceivedInvalidNetworkMessages: cumulative count of received
        /// frames that failed structural decoding before any DataSetMessage
        /// could be extracted (wrong magic, unsupported version, truncated
        /// header, etc.).
        /// </summary>
        ReceivedInvalidNetworkMessages,

        /// <summary>
        /// ReceivedDataSetMessages: cumulative count of DataSetMessages
        /// successfully decoded out of inbound NetworkMessages and routed
        /// to a DataSetReader.
        /// </summary>
        ReceivedDataSetMessages,

        /// <summary>
        /// FailedDataSetMessages: cumulative count of DataSetMessages that
        /// were decoded structurally but could not be applied (metadata
        /// version mismatch, field encoding mismatch, target-variable
        /// resolve failure, sink rejection).
        /// </summary>
        FailedDataSetMessages,

        /// <summary>
        /// SentNetworkMessages: cumulative count of NetworkMessages
        /// successfully handed to the transport for emission.
        /// </summary>
        SentNetworkMessages,

        /// <summary>
        /// SentDataSetMessages: cumulative count of DataSetMessages packed
        /// into outbound NetworkMessages (a single NetworkMessage may
        /// carry several DataSetMessages).
        /// </summary>
        SentDataSetMessages,

        /// <summary>
        /// EncryptionErrors: cumulative count of failures in the
        /// confidentiality layer (AES-CTR encrypt / decrypt error,
        /// unsupported algorithm).
        /// </summary>
        EncryptionErrors,

        /// <summary>
        /// SecurityTokenErrors: cumulative count of received frames whose
        /// SecurityTokenId could not be resolved against the current key
        /// ring (unknown token id, expired token id outside reception
        /// window).
        /// </summary>
        SecurityTokenErrors,

        /// <summary>
        /// SignatureErrors: cumulative count of received frames that
        /// failed signature verification (HMAC mismatch, truncated
        /// signature region).
        /// </summary>
        SignatureErrors,

        /// <summary>
        /// ReplayErrors: cumulative count of received frames rejected by
        /// the per-writer-group security token window because their
        /// SequenceNumber / nonce indicates replay or duplicate delivery.
        /// </summary>
        ReplayErrors,

        /// <summary>
        /// ResolverErrors: cumulative count of metadata-resolver failures
        /// (DataSetMetaData not found, MajorVersion mismatch unresolvable
        /// against the registry).
        /// </summary>
        ResolverErrors,

        /// <summary>
        /// MessageReceiveTimeouts: cumulative count of DataSetReader
        /// receive-timeouts elapsing without an inbound DataSetMessage,
        /// per Part 14 §6.2.9.6.
        /// </summary>
        MessageReceiveTimeouts,

        /// <summary>
        /// ChunksReceived: cumulative count of UADP chunked-message
        /// fragments received pending reassembly.
        /// </summary>
        ChunksReceived,

        /// <summary>
        /// ChunksReassembled: cumulative count of UADP chunked-message
        /// payloads successfully reassembled from their fragments.
        /// </summary>
        ChunksReassembled,

        /// <summary>
        /// ChunksDiscarded: cumulative count of UADP chunked-message
        /// fragments dropped due to duplicate, overlap, or size-cap
        /// violation.
        /// </summary>
        ChunksDiscarded,

        /// <summary>
        /// ChunkTimeouts: cumulative count of UADP reassembly contexts
        /// that timed out before all expected fragments arrived.
        /// </summary>
        ChunkTimeouts
    }
}
