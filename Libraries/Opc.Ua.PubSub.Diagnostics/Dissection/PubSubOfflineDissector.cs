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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using PubSubJsonDecoder = Opc.Ua.PubSub.Encoding.Json.JsonDecoder;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// Offline PubSub dissector that projects captured NetworkMessage bytes
    /// into decoded DataSets when the message is cleartext.
    /// </summary>
    public sealed class PubSubOfflineDissector
    {
        /// <summary>
        /// Initializes a new offline dissector with an empty metadata registry.
        /// </summary>
        public PubSubOfflineDissector()
            : this(CreateDefaultContext())
        {
        }

        /// <summary>
        /// Initializes a new offline dissector with the supplied decode context.
        /// </summary>
        /// <param name="context">PubSub decoder context.</param>
        public PubSubOfflineDissector(PubSubNetworkMessageContext context)
            : this(context, keyResolver: null, securityGroupId: null, securityPolicyUri: null)
        {
        }

        /// <summary>
        /// Initializes a new offline dissector with optional key resolution for secured UADP frames.
        /// </summary>
        /// <param name="context">PubSub decoder context.</param>
        /// <param name="keyResolver">Key resolver used for offline decryption.</param>
        /// <param name="securityGroupId">SecurityGroupId to prefer when resolving keys.</param>
        /// <param name="securityPolicyUri">Security policy URI to prefer when resolving keys.</param>
        public PubSubOfflineDissector(
            PubSubNetworkMessageContext context,
            IPubSubKeyResolver? keyResolver,
            string? securityGroupId = null,
            string? securityPolicyUri = null)
        {
            ArgumentNullException.ThrowIfNull(context);
            m_context = context;
            m_keyResolver = keyResolver;
            m_securityGroupId = securityGroupId;
            m_securityPolicyUri = securityPolicyUri;
        }

        /// <summary>
        /// Dissects a captured PubSub frame. Malformed input is returned as an
        /// undecodable result instead of throwing.
        /// </summary>
        /// <param name="frame">Captured frame.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The dissection result.</returns>
        public async ValueTask<PubSubDissectionResult> DissectAsync(
            PubSubCaptureFrame frame,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PubSubDissectionMessageType mapping = DetectMessageType(in frame);
            if (mapping == PubSubDissectionMessageType.Uadp)
            {
                return await DissectUadpAsync(
                    frame,
                    m_keyResolver,
                    m_securityGroupId,
                    m_securityPolicyUri,
                    cancellationToken).ConfigureAwait(false);
            }
            if (mapping == PubSubDissectionMessageType.Json)
            {
                return await DissectJsonAsync(frame, cancellationToken).ConfigureAwait(false);
            }
            return CreateUndecodable(frame, mapping, "PubSub message mapping could not be determined.");
        }

        /// <summary>
        /// Dissects a captured PubSub frame using the supplied key resolver for this call.
        /// </summary>
        /// <param name="frame">Captured frame.</param>
        /// <param name="keyResolver">Key resolver used for offline decryption.</param>
        /// <param name="securityGroupId">SecurityGroupId to prefer when resolving keys.</param>
        /// <param name="securityPolicyUri">Security policy URI to prefer when resolving keys.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The dissection result.</returns>
        public async ValueTask<PubSubDissectionResult> DissectAsync(
            PubSubCaptureFrame frame,
            IPubSubKeyResolver? keyResolver,
            string? securityGroupId = null,
            string? securityPolicyUri = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PubSubDissectionMessageType mapping = DetectMessageType(in frame);
            if (mapping == PubSubDissectionMessageType.Uadp)
            {
                return await DissectUadpAsync(
                    frame,
                    keyResolver,
                    securityGroupId,
                    securityPolicyUri,
                    cancellationToken).ConfigureAwait(false);
            }
            if (mapping == PubSubDissectionMessageType.Json)
            {
                return await DissectJsonAsync(frame, cancellationToken).ConfigureAwait(false);
            }
            return CreateUndecodable(frame, mapping, "PubSub message mapping could not be determined.");
        }

        private static PubSubNetworkMessageContext CreateDefaultContext()
        {
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                TimeProvider.System);
        }

        private async ValueTask<PubSubDissectionResult> DissectUadpAsync(
            PubSubCaptureFrame frame,
            IPubSubKeyResolver? keyResolver,
            string? securityGroupId,
            string? securityPolicyUri,
            CancellationToken cancellationToken)
        {
            try
            {
                if (TryDetectSecuredUadp(in frame, out SecuredUadpInfo secured))
                {
                    if (keyResolver is null || !secured.Encrypted)
                    {
                        return secured.Result;
                    }
                    return await TryDecryptUadpAsync(
                        frame,
                        secured,
                        keyResolver,
                        securityGroupId,
                        securityPolicyUri,
                        cancellationToken).ConfigureAwait(false);
                }

                PubSubNetworkMessage? message = UadpDecoder.Decode(frame.Data, m_context);
                if (message is null)
                {
                    return CreateUndecodable(frame, PubSubDissectionMessageType.Uadp, "UADP decoder rejected the frame.");
                }
                PubSubDissectionMessageType messageType = message.DataSetMessages.Count == 0
                    ? PubSubDissectionMessageType.Discovery
                    : PubSubDissectionMessageType.Uadp;
                return Project(frame, message, messageType);
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException)
            {
                return CreateUndecodable(frame, PubSubDissectionMessageType.Uadp, ex.Message);
            }
        }

        private async ValueTask<PubSubDissectionResult> DissectJsonAsync(
            PubSubCaptureFrame frame,
            CancellationToken cancellationToken)
        {
            try
            {
                PubSubNetworkMessage? message = await m_jsonDecoder.TryDecodeAsync(
                    frame.Data,
                    m_context,
                    cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    return CreateUndecodable(frame, PubSubDissectionMessageType.Json, "JSON decoder rejected the frame.");
                }
                PubSubDissectionMessageType messageType = message.DataSetMessages.Count == 0
                    ? PubSubDissectionMessageType.Discovery
                    : PubSubDissectionMessageType.Json;
                return Project(frame, message, messageType);
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException)
            {
                return CreateUndecodable(frame, PubSubDissectionMessageType.Json, ex.Message);
            }
        }

        private async ValueTask<PubSubDissectionResult> TryDecryptUadpAsync(
            PubSubCaptureFrame frame,
            SecuredUadpInfo secured,
            IPubSubKeyResolver keyResolver,
            string? securityGroupId,
            string? securityPolicyUri,
            CancellationToken cancellationToken)
        {
            PubSubKeyMaterial? keyMaterial = null;
            try
            {
                keyMaterial = await ResolveKeyMaterialAsync(
                    keyResolver,
                    securityGroupId,
                    secured.SecurityTokenId,
                    securityPolicyUri,
                    cancellationToken).ConfigureAwait(false);
                if (keyMaterial is null)
                {
                    return secured.Result;
                }

                IPubSubSecurityPolicy? policy = PubSubSecurityPolicyRegistry.GetByUri(keyMaterial.SecurityPolicyUri);
                if (policy is null)
                {
                    return secured.Result with
                    {
                        DiagnosticMessage = "decryption failed: unsupported PubSub security policy."
                    };
                }

                using DecryptWrapperLease wrapperLease = CreateDecryptWrapper(keyMaterial, policy);
                UadpSecurityWrapper.UnwrapResult unwrap = await wrapperLease.Wrapper.TryUnwrapAsync(
                    frame.Data.Slice(0, secured.PrefixLength),
                    frame.Data.Slice(secured.PrefixLength),
                    cancellationToken).ConfigureAwait(false);
                if (!unwrap.IsSuccess || !unwrap.InnerPayload.HasValue)
                {
                    return secured.Result with
                    {
                        DiagnosticMessage = "decryption failed: " + (unwrap.Reason ?? "UADP unwrap failed.")
                    };
                }

                byte[] cleartext = new byte[secured.PrefixLength + unwrap.InnerPayload.Value.Length];
                frame.Data.Span.Slice(0, secured.PrefixLength).CopyTo(cleartext);
                unwrap.InnerPayload.Value.Span.CopyTo(cleartext.AsSpan(secured.PrefixLength));
                var clearFrame = new PubSubCaptureFrame(
                    frame.Timestamp,
                    frame.Direction,
                    frame.TransportProfileUri,
                    cleartext,
                    frame.Endpoint,
                    frame.Topic);
                PubSubNetworkMessage? message = UadpDecoder.Decode(clearFrame.Data, m_context);
                if (message is null)
                {
                    return secured.Result with
                    {
                        DiagnosticMessage = "decryption failed: recovered UADP payload could not be decoded."
                    };
                }

                PubSubDissectionMessageType messageType = message.DataSetMessages.Count == 0
                    ? PubSubDissectionMessageType.Discovery
                    : PubSubDissectionMessageType.Uadp;
                return Project(
                    frame,
                    message,
                    messageType,
                    secured.SecurityState,
                    secured.SecurityTokenId,
                    "decrypted");
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException)
            {
                return secured.Result with
                {
                    DiagnosticMessage = "decryption failed: " + ex.Message
                };
            }
            finally
            {
                keyMaterial?.Dispose();
            }
        }

        private static async ValueTask<PubSubKeyMaterial?> ResolveKeyMaterialAsync(
            IPubSubKeyResolver keyResolver,
            string? securityGroupId,
            uint tokenId,
            string? securityPolicyUri,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(securityPolicyUri))
            {
                return await keyResolver.TryResolveAsync(
                    securityGroupId,
                    tokenId,
                    securityPolicyUri,
                    cancellationToken).ConfigureAwait(false);
            }

            IPubSubSecurityPolicy[] policies = [.. PubSubSecurityPolicyRegistry.All];
            for (int index = 0; index < policies.Length; index++)
            {
                IPubSubSecurityPolicy policy = policies[index];
                if (string.Equals(policy.PolicyUri, PubSubSecurityPolicyUri.None, StringComparison.Ordinal))
                {
                    continue;
                }
                PubSubKeyMaterial? material = await keyResolver.TryResolveAsync(
                    securityGroupId,
                    tokenId,
                    policy.PolicyUri,
                    cancellationToken).ConfigureAwait(false);
                if (material is not null)
                {
                    return material;
                }
            }
            return null;
        }

        private static DecryptWrapperLease CreateDecryptWrapper(
            PubSubKeyMaterial material,
            IPubSubSecurityPolicy policy)
        {
            return new DecryptWrapperLease(material, policy);
        }

        private static bool TryDetectSecuredUadp(
            in PubSubCaptureFrame frame,
            out SecuredUadpInfo result)
        {
            result = default!;
            if (!UadpDecoder.TryReadOuterPrefix(
                frame.Data,
                out int prefixLength,
                out bool securityEnabled,
                out PublisherId publisherId,
                out ushort writerGroupId) ||
                !securityEnabled)
            {
                return false;
            }

            ReadOnlySpan<byte> data = frame.Data.Span;
            if (prefixLength > data.Length ||
                !UadpSecurityHeader.TryRead(
                    data[prefixLength..],
                    out UadpSecurityHeader header,
                    out _))
            {
                result = new SecuredUadpInfo(
                    CreateUndecodable(
                    frame,
                    PubSubDissectionMessageType.Uadp,
                    "UADP SecurityHeader is malformed or truncated."),
                    prefixLength,
                    SecurityTokenId: 0,
                    Encrypted: false,
                    PubSubDissectionSecurityState.None);
                return true;
            }

            var flags = (UadpSecurityFlagsEncodingMask)header.SecurityFlags;
            bool encrypted = (flags & UadpSecurityFlagsEncodingMask.NetworkMessageEncrypted) != 0;
            bool signed = (flags & UadpSecurityFlagsEncodingMask.NetworkMessageSigned) != 0;
            PubSubDissectionSecurityState securityState = encrypted
                ? PubSubDissectionSecurityState.Encrypted
                : PubSubDissectionSecurityState.Signed;
            PubSubDissectionResult dissection = new()
            {
                Timestamp = frame.Timestamp,
                Direction = frame.Direction,
                TransportProfileUri = frame.TransportProfileUri,
                Endpoint = frame.Endpoint,
                Topic = frame.Topic,
                PayloadLength = frame.Data.Length,
                MessageType = PubSubDissectionMessageType.Uadp,
                SecurityState = securityState,
                PublisherId = publisherId,
                WriterGroupId = writerGroupId == 0 ? null : writerGroupId,
                SecurityTokenId = header.SecurityTokenId,
                IsDecoded = false,
                IsUndecodable = false,
                DiagnosticMessage = encrypted || signed
                    ? "encrypted (key required)"
                    : "SecurityHeader present with no signing or encryption flags."
            };
            result = new SecuredUadpInfo(
                dissection,
                prefixLength,
                header.SecurityTokenId,
                encrypted,
                securityState);
            return true;
        }

        private static PubSubDissectionResult Project(
            in PubSubCaptureFrame frame,
            PubSubNetworkMessage message,
            PubSubDissectionMessageType messageType,
            PubSubDissectionSecurityState securityState = PubSubDissectionSecurityState.None,
            uint? securityTokenId = null,
            string? diagnosticMessage = null)
        {
            List<ushort> writerIds = [];
            List<PubSubDissectedDataSet> dataSets = [];
            foreach (PubSubDataSetMessage dataSetMessage in message.DataSetMessages)
            {
                writerIds.Add(dataSetMessage.DataSetWriterId);
                dataSets.Add(ProjectDataSet(dataSetMessage));
            }

            return new PubSubDissectionResult
            {
                Timestamp = frame.Timestamp,
                Direction = frame.Direction,
                TransportProfileUri = frame.TransportProfileUri,
                Endpoint = frame.Endpoint,
                Topic = frame.Topic,
                PayloadLength = frame.Data.Length,
                MessageType = messageType,
                SecurityState = securityState,
                PublisherId = message.PublisherId,
                WriterGroupId = message.WriterGroupId,
                DataSetWriterIds = [.. writerIds],
                DataSets = [.. dataSets],
                SecurityTokenId = securityTokenId,
                IsDecoded = true,
                IsUndecodable = false,
                DiagnosticMessage = diagnosticMessage
            };
        }

        private static PubSubDissectedDataSet ProjectDataSet(PubSubDataSetMessage dataSetMessage)
        {
            List<PubSubDissectedField> fields = [];
            foreach (DataSetField field in dataSetMessage.Fields)
            {
                fields.Add(new PubSubDissectedField
                {
                    Name = field.Name,
                    Value = field.Value,
                    StatusCode = field.StatusCode,
                    Encoding = field.Encoding
                });
            }

            return new PubSubDissectedDataSet
            {
                DataSetWriterId = dataSetMessage.DataSetWriterId,
                SequenceNumber = dataSetMessage.SequenceNumber,
                MessageType = dataSetMessage.MessageType,
                Status = dataSetMessage.Status,
                Fields = [.. fields]
            };
        }

        private static PubSubDissectionResult CreateUndecodable(
            in PubSubCaptureFrame frame,
            PubSubDissectionMessageType mapping,
            string diagnosticMessage)
        {
            return new PubSubDissectionResult
            {
                Timestamp = frame.Timestamp,
                Direction = frame.Direction,
                TransportProfileUri = frame.TransportProfileUri,
                Endpoint = frame.Endpoint,
                Topic = frame.Topic,
                PayloadLength = frame.Data.Length,
                MessageType = mapping,
                SecurityState = PubSubDissectionSecurityState.None,
                PublisherId = PublisherId.Null,
                IsDecoded = false,
                IsUndecodable = true,
                DiagnosticMessage = diagnosticMessage
            };
        }

        private static PubSubDissectionMessageType DetectMessageType(in PubSubCaptureFrame frame)
        {
            string profile = frame.TransportProfileUri;
            if (profile.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                return PubSubDissectionMessageType.Json;
            }
            if (profile.Contains("uadp", StringComparison.OrdinalIgnoreCase) ||
                profile.Contains("udp", StringComparison.OrdinalIgnoreCase))
            {
                return PubSubDissectionMessageType.Uadp;
            }
            ReadOnlySpan<byte> data = frame.Data.Span;
            for (int i = 0; i < data.Length; i++)
            {
                byte value = data[i];
                if (value == (byte)'{' || value == (byte)'[')
                {
                    return PubSubDissectionMessageType.Json;
                }
                if (!char.IsWhiteSpace((char)value))
                {
                    return PubSubDissectionMessageType.Uadp;
                }
            }
            return PubSubDissectionMessageType.Unknown;
        }

        private readonly PubSubNetworkMessageContext m_context;
        private readonly IPubSubKeyResolver? m_keyResolver;
        private readonly string? m_securityGroupId;
        private readonly string? m_securityPolicyUri;
        private readonly PubSubJsonDecoder m_jsonDecoder = new();

        private sealed record SecuredUadpInfo(
            PubSubDissectionResult Result,
            int PrefixLength,
            uint SecurityTokenId,
            bool Encrypted,
            PubSubDissectionSecurityState SecurityState);

        private sealed class OfflineTelemetryContext : TelemetryContextBase
        {
            private OfflineTelemetryContext()
                : base(NullLoggerFactory.Instance)
            {
            }

            public static OfflineTelemetryContext Instance { get; } = new();
        }

        private sealed class DecryptWrapperLease : IDisposable
        {
            [System.Diagnostics.CodeAnalysis.SuppressMessage(
                "Reliability",
                "CA2000:Dispose objects before losing scope",
                Justification = "TODO: PubSubSecurityKey ownership transfers to PubSubSecurityKeyRing.SetCurrent.")]
            public DecryptWrapperLease(PubSubKeyMaterial material, IPubSubSecurityPolicy policy)
            {
                m_ring = new PubSubSecurityKeyRing(material.SecurityGroupId);
                var key = new PubSubSecurityKey(
                    material.TokenId,
                    ByteString.Create(material.SigningKey.ToArray()),
                    ByteString.Create(material.EncryptingKey.ToArray()),
                    ByteString.Create(material.KeyNonce.ToArray()),
                    DateTimeUtc.From(DateTime.UtcNow),
                    TimeSpan.FromDays(1));
                m_ring.SetCurrent(key);
                m_nonceProvider = new RandomNonceProvider(PublisherId.FromUInt32(0U));
                var window = new SecurityTokenWindow();
                window.RegisterToken(material.TokenId);
                Wrapper = new UadpSecurityWrapper(
                    policy,
                    new StaticSecurityKeyProvider(material.SecurityGroupId, m_ring),
                    m_nonceProvider,
                    window,
                    OfflineTelemetryContext.Instance);
            }

            public UadpSecurityWrapper Wrapper { get; }

            public void Dispose()
            {
                m_nonceProvider.Dispose();
                m_ring.Dispose();
            }

            private readonly PubSubSecurityKeyRing m_ring;
            private readonly RandomNonceProvider m_nonceProvider;
        }
    }
}
