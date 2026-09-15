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
using Opc.Ua;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Json;
using Opc.Ua.PubSub.Encoding.Uadp;

namespace UaLens.Plugins.PubSub;

/// <summary>
/// Adds document budgets/evidence around the actual stack decoder. No wire parsing,
/// message-security bypass, or retention of raw frames is performed here.
/// </summary>
internal sealed class PubSubObservedDecoder : INetworkMessageDecoder
{
    public PubSubObservedDecoder(INetworkMessageDecoder decoder, PubSubObservationStore store, int maxBytes)
    {
        m_decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        m_store = store ?? throw new ArgumentNullException(nameof(store));
        m_maxBytes = maxBytes;
    }

    public string TransportProfileUri => m_decoder.TransportProfileUri;

    public async ValueTask<PubSubNetworkMessage?> TryDecodeAsync(
        ReadOnlyMemory<byte> frame,
        PubSubNetworkMessageContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (frame.Length > m_maxBytes)
        {
            context.Diagnostics.Increment(PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
            m_store.RecordEvidence("Decoder", StatusCodes.BadEncodingLimitsExceeded,
                "The received frame exceeded this document's configured byte limit.");
            return null;
        }
        var observedContext = new PubSubNetworkMessageContext(
            context.MessageContext,
            context.MetaDataRegistry,
            new PubSubObservedDiagnostics(context.Diagnostics, m_store),
            context.TimeProvider,
            context.UadpActionFieldEncoding);
        PubSubNetworkMessage? message = await m_decoder.TryDecodeAsync(frame, observedContext, cancellationToken)
            .ConfigureAwait(false);
        if (message is null)
        {
            m_store.RecordEvidence("Decoder", StatusCodes.BadDecodingError,
                "The stack decoder rejected this payload. Consult decoder, metadata and security counters.");
            return null;
        }
        if (message.DataSetMessages.Count > PubSubConfigurationValidation.MaxFields ||
            message.DataSetMessages.Contains(
                dataSet => dataSet.Fields.Count > PubSubConfigurationValidation.MaxFields))
        {
            context.Diagnostics.Increment(PubSubDiagnosticsCounterKind.FailedDataSetMessages);
            m_store.RecordEvidence("Decoder", StatusCodes.BadEncodingLimitsExceeded,
                "The decoded payload exceeded the document's dataset/field budget.");
            return null;
        }
        if (message is UadpDiscoveryResponseMessage or JsonDiscoveryMessage or JsonMetaDataMessage &&
            !m_store.AcceptDiscoveryResponse())
        {
            return null;
        }
        return message;
    }

    private readonly INetworkMessageDecoder m_decoder;
    private readonly PubSubObservationStore m_store;
    private readonly int m_maxBytes;
}

internal sealed class PubSubObservedDiagnostics : IPubSubDiagnostics
{
    public PubSubObservedDiagnostics(IPubSubDiagnostics inner, PubSubObservationStore store)
    {
        m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
        m_store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public PubSubDiagnosticsLevel Level => m_inner.Level;

    public void Increment(PubSubDiagnosticsCounterKind kind, long delta = 1)
    {
        m_inner.Increment(kind, delta);
    }

    public long Read(PubSubDiagnosticsCounterKind kind)
    {
        return m_inner.Read(kind);
    }

    public void RecordError(StatusCode statusCode, string message)
    {
        m_inner.RecordError(statusCode, message);
        m_store.RecordEvidence("Decoder", statusCode,
            "The stack reported a decoding error; peer-supplied text and raw payloads are not retained.");
    }

    public void Reset()
    {
        m_inner.Reset();
    }

    private readonly IPubSubDiagnostics m_inner;
    private readonly PubSubObservationStore m_store;
}
