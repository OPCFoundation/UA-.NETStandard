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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Default frame-level <see cref="ITranscoder"/>. Applies the
    /// structured transcode (transforms + projection), encodes the target
    /// to its wire form, and manages re-securing across the boundary. A
    /// same-encoding identity route with an available raw frame is served
    /// by a zero-copy passthrough fast path.
    /// </summary>
    public sealed class PubSubTranscoder : ITranscoder
    {
        private readonly NetworkMessageTranscoder m_structured;
        private readonly IReadOnlyDictionary<string, INetworkMessageEncoder> m_encoders;
        private readonly TranscodeContext m_context;
        private readonly TranscodeSecurity m_security;
        private readonly TranscodeEncoding m_targetEncoding;
        private readonly bool m_identity;
        private readonly TranscodePromotion? m_promotion;
        private readonly ILogger m_logger;
        private readonly IPubSubDiagnostics m_diagnostics;

        /// <summary>
        /// Initializes a new <see cref="PubSubTranscoder"/>.
        /// </summary>
        /// <param name="spec">Transcode specification.</param>
        /// <param name="encoders">
        /// NetworkMessage encoders keyed by transport profile URI.
        /// </param>
        /// <param name="context">Per-run transcode environment.</param>
        /// <param name="security">
        /// Security context, or <see langword="null"/> for an unsecured
        /// transcode.
        /// </param>
        /// <param name="projector">
        /// Profile projector, or <see langword="null"/> for the default.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when a required dependency is <see langword="null"/>.
        /// </exception>
        public PubSubTranscoder(
            TranscodeSpec spec,
            IReadOnlyDictionary<string, INetworkMessageEncoder> encoders,
            TranscodeContext context,
            TranscodeSecurity? security = null,
            INetworkMessageProfileProjector? projector = null)
        {
            if (spec is null)
            {
                throw new ArgumentNullException(nameof(spec));
            }
            m_encoders = encoders ?? throw new ArgumentNullException(nameof(encoders));
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_structured = new NetworkMessageTranscoder(spec, projector);
            m_security = security ?? TranscodeSecurity.None;
            m_targetEncoding = spec.TargetEncoding;
            m_identity = spec.IsIdentity;
            m_promotion = spec.Promotion is { HasFields: true } p ? p : null;
            m_logger = context.Telemetry.CreateLogger<PubSubTranscoder>();
            m_diagnostics = context.EncodingContext.Diagnostics;
        }

        /// <inheritdoc/>
        public async ValueTask<TranscodeResult> TranscodeAsync(
            TranscodeInput input,
            CancellationToken cancellationToken = default)
        {
            PubSubNetworkMessage source = input.Message
                ?? throw new ArgumentException(
                    "TranscodeInput.Message must not be null.", nameof(input));

            TranscodeEncoding sourceEncoding = source.EncodingOf();

            if (m_identity &&
                sourceEncoding == m_targetEncoding &&
                !input.SourceFrame.IsEmpty &&
                !input.SourceFrameSecured &&
                !m_security.IsTargetSecured)
            {
                return new TranscodeResult
                {
                    Frames = [input.SourceFrame],
                    Messages = [source],
                    FastPath = true
                };
            }

            if (m_security.WouldRefuseDowngrade(input.SourceFrameSecured, m_targetEncoding))
            {
                m_diagnostics.Increment(PubSubDiagnosticsCounterKind.EncryptionErrors);
                m_diagnostics.RecordError(
                    StatusCodes.BadSecurityModeRejected,
                    "Refusing to transcode a secured message to an output without " +
                    "message-layer security (set AllowInsecureCrossEncoding to override).");
                m_logger.LogWarning(
                    "Dropping transcode: secured source would be downgraded to an " +
                    "unsecured {Encoding} target.",
                    m_targetEncoding);
                return TranscodeResult.Empty;
            }

            ArrayOf<PubSubNetworkMessage> targets = await m_structured
                .TranscodeAsync(source, m_context, cancellationToken)
                .ConfigureAwait(false);
            if (targets.Count == 0)
            {
                return TranscodeResult.Empty;
            }

            INetworkMessageEncoder? encoder = ResolveEncoder(m_targetEncoding);
            if (encoder is null)
            {
                m_logger.LogWarning(
                    "No encoder registered for {Encoding}; transcode dropped.",
                    m_targetEncoding);
                return TranscodeResult.Empty;
            }

            var frames = new List<ReadOnlyMemory<byte>>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                frames.Add(await EncodeAsync(targets[i], encoder, cancellationToken)
                    .ConfigureAwait(false));
            }
            return new TranscodeResult
            {
                Frames = frames,
                Messages = targets,
                Properties = BuildPromotedProperties(targets)
            };
        }

        private ArrayOf<PubSubMessageProperty> BuildPromotedProperties(
            ArrayOf<PubSubNetworkMessage> targets)
        {
            if (m_promotion is null)
            {
                return [];
            }
            ArrayOf<string> names = m_promotion.FieldNames;
            string prefix = m_promotion.PropertyKeyPrefix ?? string.Empty;
            var properties = new List<PubSubMessageProperty>(names.Count);
            for (int n = 0; n < names.Count; n++)
            {
                string fieldName = names[n];
                if (string.IsNullOrEmpty(fieldName) ||
                    !TryFindField(targets, fieldName, out DataSetField field))
                {
                    continue;
                }
                string value = field.Value.ToString(null, CultureInfo.InvariantCulture);
                properties.Add(new PubSubMessageProperty(prefix + fieldName, value));
            }
            return properties;
        }

        private static bool TryFindField(
            ArrayOf<PubSubNetworkMessage> targets,
            string fieldName,
            out DataSetField field)
        {
            for (int m = 0; m < targets.Count; m++)
            {
                ArrayOf<PubSubDataSetMessage> messages = targets[m].DataSetMessages;
                for (int d = 0; d < messages.Count; d++)
                {
                    ArrayOf<DataSetField> fields = messages[d].Fields;
                    for (int f = 0; f < fields.Count; f++)
                    {
                        if (string.Equals(fields[f].Name, fieldName, StringComparison.Ordinal))
                        {
                            field = fields[f];
                            return true;
                        }
                    }
                }
            }
            field = null!;
            return false;
        }

        private ValueTask<ReadOnlyMemory<byte>> EncodeAsync(
            PubSubNetworkMessage message,
            INetworkMessageEncoder encoder,
            CancellationToken cancellationToken)
        {
            if (m_targetEncoding == TranscodeEncoding.Uadp &&
                m_security.IsTargetSecured &&
                message is UadpNetworkMessage uadp)
            {
                ReadOnlyMemory<byte> encoded = UadpEncoder.EncodeWithSecurityBoundary(
                    uadp, m_context.EncodingContext, out int payloadOffset);
                return m_security.WrapUadpAsync(encoded, payloadOffset, cancellationToken);
            }
            return encoder.EncodeAsync(message, m_context.EncodingContext, cancellationToken);
        }

        private INetworkMessageEncoder? ResolveEncoder(TranscodeEncoding encoding)
        {
            string profile = encoding.ToTransportProfileUri();
            if (m_encoders.TryGetValue(profile, out INetworkMessageEncoder? exact))
            {
                return exact;
            }
            foreach (KeyValuePair<string, INetworkMessageEncoder> entry in m_encoders)
            {
                if (entry.Key.FromTransportProfileUri() == encoding)
                {
                    return entry.Value;
                }
            }
            return null;
        }
    }
}
