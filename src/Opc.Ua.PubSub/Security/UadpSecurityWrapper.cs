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
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// Bridges the UADP encoder/decoder with the
    /// security subsystem. Wraps an unsecured NetworkMessage with the
    /// SecurityHeader, encrypts the payload, and appends the
    /// signature; on receive does the inverse plus replay-window and
    /// nonce-reuse checks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements the receive- and send-side processing flow described
    /// by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3">
    /// Part 14 §7.2.4.4.3 PubSub message security</see>, with the byte
    /// layouts taken from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.1.6">
    /// Annex A.2.1.6 (signed and encrypted)</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.2.5">
    /// Annex A.2.2.5 (signed only)</see>.
    /// </para>
    /// <para>
    /// The wrapper is stateless on send; replay protection is enforced
    /// on the receive side via the supplied <see cref="ISecurityTokenWindow"/>.
    /// Callers split the unwrapped UADP NetworkMessage into the outer
    /// prefix (UadpFlags + ExtendedFlags + PublisherId + headers) and
    /// the inner payload (GroupHeader + PayloadHeader + DataSetMessages
    /// + Padding) before invoking <see cref="WrapAsync"/>; the prefix
    /// is unmodified by the wrapper, the payload is encrypted, and the
    /// signature covers the entire authenticated portion as required
    /// by Annex A.
    /// </para>
    /// </remarks>
    public sealed class UadpSecurityWrapper
    {
        private readonly IPubSubSecurityKeyProvider m_keyProvider;
        private readonly INonceProvider m_nonceProvider;
        private readonly ISecurityTokenWindow m_tokenWindow;
        private readonly ILogger m_logger;
        private readonly IPubSubSecurityEventSink? m_securityEventSink;

        /// <summary>
        /// Initializes a new <see cref="UadpSecurityWrapper"/>.
        /// </summary>
        /// <param name="policy">Security policy bundle.</param>
        /// <param name="keyProvider">Key provider for the SecurityGroup.</param>
        /// <param name="nonceProvider">Per-message nonce generator.</param>
        /// <param name="tokenWindow">Receive-side replay window.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="securityEventSink">Optional structured security-event sink.</param>
        public UadpSecurityWrapper(
            IPubSubSecurityPolicy policy,
            IPubSubSecurityKeyProvider keyProvider,
            INonceProvider nonceProvider,
            ISecurityTokenWindow tokenWindow,
            ITelemetryContext telemetry,
            IPubSubSecurityEventSink? securityEventSink = null)
        {
            if (policy is null)
            {
                throw new ArgumentNullException(nameof(policy));
            }
            if (keyProvider is null)
            {
                throw new ArgumentNullException(nameof(keyProvider));
            }
            if (nonceProvider is null)
            {
                throw new ArgumentNullException(nameof(nonceProvider));
            }
            if (tokenWindow is null)
            {
                throw new ArgumentNullException(nameof(tokenWindow));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            Policy = policy;
            m_keyProvider = keyProvider;
            m_nonceProvider = nonceProvider;
            m_tokenWindow = tokenWindow;
            m_logger = telemetry.CreateLogger<UadpSecurityWrapper>();
            m_securityEventSink = securityEventSink;
        }

        /// <summary>
        /// The active policy bundle.
        /// </summary>
        public IPubSubSecurityPolicy Policy { get; }

        /// <summary>
        /// Wraps an unsecured NetworkMessage. Caller supplies the
        /// outer prefix (already encoded) and the inner payload (the
        /// portion to be encrypted) — the prefix is concatenated as-is
        /// in front of the SecurityHeader, the payload is replaced by
        /// the ciphertext, and the signature is appended.
        /// </summary>
        /// <param name="outerPrefix">Outer UADP prefix bytes.</param>
        /// <param name="innerPayload">Inner payload bytes.</param>
        /// <param name="options">Sign/encrypt selection (default
        /// <see cref="UadpSecurityWrapOptions.SignAndEncrypt"/>).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// The wrapped message bytes:
        /// <c>[outerPrefix || SecurityHeader || ciphertext || signature]</c>.
        /// </returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async ValueTask<ReadOnlyMemory<byte>> WrapAsync(
            ReadOnlyMemory<byte> outerPrefix,
            ReadOnlyMemory<byte> innerPayload,
            UadpSecurityWrapOptions options = UadpSecurityWrapOptions.SignAndEncrypt,
            CancellationToken cancellationToken = default)
        {
            PubSubSecurityKey key = await m_keyProvider
                .GetCurrentKeyAsync(cancellationToken)
                .ConfigureAwait(false);

            bool sign = options is UadpSecurityWrapOptions.SignOnly
                or UadpSecurityWrapOptions.SignAndEncrypt;
            bool encrypt = options is UadpSecurityWrapOptions.EncryptOnly
                or UadpSecurityWrapOptions.SignAndEncrypt;

            byte[] nonceBytes = Policy.NonceLength == 0
                ? []
                : new byte[Policy.NonceLength];
            if (Policy.NonceLength != 0)
            {
                m_nonceProvider.GetNext(key.TokenId, key.KeyNonce.Span, nonceBytes);
            }

            UadpSecurityFlagsEncodingMask flagsMask = 0;
            if (sign)
            {
                flagsMask |= UadpSecurityFlagsEncodingMask.NetworkMessageSigned;
            }
            if (encrypt)
            {
                flagsMask |= UadpSecurityFlagsEncodingMask.NetworkMessageEncrypted;
            }
            byte flags = (byte)flagsMask;

            var header = new UadpSecurityHeader(
                flags,
                key.TokenId,
                nonceBytes);

            int headerSize = header.GetEncodedSize();
            int signatureLength = sign ? Policy.SignatureLength : 0;
            int totalSize = outerPrefix.Length + headerSize + innerPayload.Length + signatureLength;
            byte[] result = new byte[totalSize];

            outerPrefix.Span.CopyTo(result.AsSpan(0, outerPrefix.Length));
            header.WriteTo(result.AsSpan(outerPrefix.Length, headerSize), out int written);
            if (written != headerSize)
            {
                throw new InvalidOperationException(
                    "SecurityHeader encoder produced an unexpected length.");
            }

            int payloadOffset = outerPrefix.Length + headerSize;
            if (encrypt && Policy.EncryptingKeyLength > 0)
            {
                Policy.Encrypt(
                    innerPayload.Span,
                    key.EncryptingKey.Span,
                    nonceBytes,
                    result.AsSpan(payloadOffset, innerPayload.Length));
            }
            else
            {
                innerPayload.Span.CopyTo(result.AsSpan(payloadOffset, innerPayload.Length));
            }

            int signedLength = outerPrefix.Length + headerSize + innerPayload.Length;
            if (sign && signatureLength > 0)
            {
                Policy.Sign(
                    result.AsSpan(0, signedLength),
                    key.SigningKey.Span,
                    result.AsSpan(signedLength, signatureLength));
            }

            m_logger.WrappedMessage(key.TokenId, options, innerPayload.Length, signedLength);

            return result;
        }

        /// <summary>
        /// Verifies, replay-checks and decrypts a previously-wrapped
        /// NetworkMessage.
        /// </summary>
        /// <param name="outerPrefix">Outer UADP prefix bytes.</param>
        /// <param name="securityAndPayload">
        /// SecurityHeader + ciphertext + signature, in that order.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// <see cref="UnwrapResult.Success"/> with the decrypted inner
        /// payload on success; otherwise an
        /// <see cref="UnwrapResult.Failure"/> describing why.
        /// </returns>
        public ValueTask<UnwrapResult> TryUnwrapAsync(
            ReadOnlyMemory<byte> outerPrefix,
            ReadOnlyMemory<byte> securityAndPayload,
            CancellationToken cancellationToken = default)
        {
            return TryUnwrapCoreAsync(
                outerPrefix,
                securityAndPayload,
                replayIdentity: null,
                MessageSecurityMode.None,
                cancellationToken);
        }

        internal ValueTask<UnwrapResult> TryUnwrapAsync(
            ReadOnlyMemory<byte> outerPrefix,
            ReadOnlyMemory<byte> securityAndPayload,
            PublisherId publisherId,
            ushort writerGroupId,
            MessageSecurityMode requiredMode,
            CancellationToken cancellationToken = default)
        {
            return TryUnwrapCoreAsync(
                outerPrefix,
                securityAndPayload,
                new ReplayIdentity(publisherId, writerGroupId),
                requiredMode,
                cancellationToken);
        }

        private async ValueTask<UnwrapResult> TryUnwrapCoreAsync(
            ReadOnlyMemory<byte> outerPrefix,
            ReadOnlyMemory<byte> securityAndPayload,
            ReplayIdentity? replayIdentity,
            MessageSecurityMode requiredMode,
            CancellationToken cancellationToken)
        {
            if (!UadpSecurityHeader.TryRead(
                securityAndPayload.Span,
                out UadpSecurityHeader header,
                out int headerLength))
            {
                m_logger.FailedToParseSecurityHeader();
                return UnwrapResult.Failure(StatusCodes.BadDecodingError, "SecurityHeader malformed");
            }

            var flagsMask = (UadpSecurityFlagsEncodingMask)header.SecurityFlags;
            bool encrypted = (flagsMask & UadpSecurityFlagsEncodingMask.NetworkMessageEncrypted) != 0;
            bool signed = (flagsMask & UadpSecurityFlagsEncodingMask.NetworkMessageSigned) != 0;

            int signatureLength = signed ? Policy.SignatureLength : 0;
            int payloadAndFooterLength = securityAndPayload.Length - headerLength - signatureLength;
            if (payloadAndFooterLength < 0)
            {
                return UnwrapResult.Failure(StatusCodes.BadDecodingError, "Truncated signed body");
            }

            PubSubSecurityKey? key = await m_keyProvider
                .TryGetKeyAsync(header.SecurityTokenId, cancellationToken)
                .ConfigureAwait(false);
            if (key is null)
            {
                m_logger.RejectedUnknownToken(header.SecurityTokenId);
                EmitSecurityEvent(new PubSubSecurityEvent(
                    PubSubSecurityEventKind.UnknownTokenRejected,
                    DateTimeOffset.UtcNow,
                    PubSubSecurityEventOutcome.Rejected,
                    tokenId: header.SecurityTokenId));
                return UnwrapResult.Failure(
                    StatusCodes.BadSecurityChecksFailed,
                    $"Unknown SecurityTokenId {header.SecurityTokenId}");
            }

            int signedLength = outerPrefix.Length + headerLength + payloadAndFooterLength;
            byte[] signedBuffer = ArrayPool<byte>.Shared.Rent(signedLength);
            try
            {
                outerPrefix.Span.CopyTo(signedBuffer.AsSpan(0, outerPrefix.Length));
                securityAndPayload
                    .Span
[..(headerLength + payloadAndFooterLength)]
                    .CopyTo(signedBuffer.AsSpan(outerPrefix.Length, headerLength + payloadAndFooterLength));

                if (signed && signatureLength > 0)
                {
                    ReadOnlySpan<byte> signature = securityAndPayload
                        .Span
                        .Slice(headerLength + payloadAndFooterLength, signatureLength);
                    bool valid = Policy.Verify(
                        signedBuffer.AsSpan(0, signedLength),
                        signature,
                        key.SigningKey.Span);
                    if (!valid)
                    {
                        m_logger.SignatureVerificationFailed(header.SecurityTokenId);
                        EmitSecurityEvent(new PubSubSecurityEvent(
                            PubSubSecurityEventKind.SignatureVerificationFailed,
                            DateTimeOffset.UtcNow,
                            PubSubSecurityEventOutcome.Failed,
                            tokenId: header.SecurityTokenId));
                        return UnwrapResult.Failure(
                            StatusCodes.BadSecurityChecksFailed,
                            "Signature verification failed");
                    }
                }

                if (!SatisfiesRequiredSecurity(requiredMode, flagsMask))
                {
                    return UnwrapResult.Failure(
                        StatusCodes.BadSecurityModeRejected,
                        "Inbound frame security level is lower than the configured SecurityMode.");
                }

                // The MessageNonce embeds a monotonic per-key
                // SequenceNumber (Part 14 Table 156: RandomBytes ||
                // SequenceNumber). The nonce is part of the signed
                // SecurityHeader, so the sequence number is
                // authenticated and available before decryption.
                // Extract it and drive the monotonic replay window with
                // it, rejecting duplicates, too-old sequences and exact
                // nonce reuse.
                ulong sequenceNumber = 0;
                ReadOnlySpan<byte> nonceSpan = header.MessageNonce.Span;
                if (nonceSpan.Length == AesCtrNonceLayout.NonceLength)
                {
                    (_, sequenceNumber) = AesCtrNonceLayout.Parse(nonceSpan);
                }

                bool replayAccepted;
                if (replayIdentity is ReplayIdentity identity)
                {
                    if (signed &&
                        signatureLength > 0 &&
                        m_tokenWindow is IScopedSecurityTokenWindow scopedWindow)
                    {
                        replayAccepted = scopedWindow.TryAccept(
                            identity.PublisherId,
                            identity.WriterGroupId,
                            header.SecurityTokenId,
                            sequenceNumber,
                            nonceSpan);
                    }
                    else if (signed && signatureLength > 0)
                    {
                        replayAccepted = m_tokenWindow.TryAccept(
                            header.SecurityTokenId,
                            sequenceNumber,
                            nonceSpan);
                    }
                    else
                    {
                        // The PublisherId, WriterGroupId and nonce are not
                        // authenticated on an unsigned frame. Do not let it
                        // mutate replay state for authenticated traffic.
                        replayAccepted = true;
                    }
                }
                else
                {
                    replayAccepted = m_tokenWindow.TryAccept(
                        header.SecurityTokenId,
                        sequenceNumber,
                        nonceSpan);
                }

                if (!replayAccepted)
                {
                    m_logger.RejectedReplayOrNonceReuse(header.SecurityTokenId, sequenceNumber);
                    EmitSecurityEvent(new PubSubSecurityEvent(
                        PubSubSecurityEventKind.ReplayRejected,
                        DateTimeOffset.UtcNow,
                        PubSubSecurityEventOutcome.Rejected,
                        tokenId: header.SecurityTokenId));
                    return UnwrapResult.Failure(
                        StatusCodes.BadSecurityChecksFailed,
                        "Replay or nonce reuse detected");
                }

                byte[] plaintext = new byte[payloadAndFooterLength];
                if (encrypted && Policy.EncryptingKeyLength > 0)
                {
                    Policy.Decrypt(
                        securityAndPayload.Span.Slice(headerLength, payloadAndFooterLength),
                        key.EncryptingKey.Span,
                        header.MessageNonce.Span,
                        plaintext);
                }
                else
                {
                    securityAndPayload
                        .Span
                        .Slice(headerLength, payloadAndFooterLength)
                        .CopyTo(plaintext);
                }

                return UnwrapResult.Success(plaintext, header);
            }
            finally
            {
                Array.Clear(signedBuffer, 0, signedLength);
                ArrayPool<byte>.Shared.Return(signedBuffer);
            }
        }

        private static bool SatisfiesRequiredSecurity(
            MessageSecurityMode requiredMode,
            UadpSecurityFlagsEncodingMask flags)
        {
            if (requiredMode is not (MessageSecurityMode.Sign
                or MessageSecurityMode.SignAndEncrypt))
            {
                return true;
            }

            bool signed = (flags & UadpSecurityFlagsEncodingMask.NetworkMessageSigned) != 0;
            bool encrypted = (flags & UadpSecurityFlagsEncodingMask.NetworkMessageEncrypted) != 0;
            return requiredMode == MessageSecurityMode.SignAndEncrypt
                ? signed && encrypted
                : signed;
        }

        private void EmitSecurityEvent(PubSubSecurityEvent securityEvent)
        {
            if (m_securityEventSink is null)
            {
                return;
            }

            try
            {
                m_securityEventSink.OnSecurityEvent(securityEvent);
            }
            catch (Exception ex)
            {
                m_logger.PubSubSecurityEventSinkRaisedException(ex);
            }
        }

        private readonly record struct ReplayIdentity(
            PublisherId PublisherId,
            ushort WriterGroupId);

        /// <summary>
        /// Outcome of
        /// <see cref="TryUnwrapAsync(ReadOnlyMemory{byte}, ReadOnlyMemory{byte}, CancellationToken)"/>.
        /// </summary>
        public sealed record UnwrapResult
        {
            private UnwrapResult(
                ReadOnlyMemory<byte>? innerPayload,
                UadpSecurityHeader? header,
                StatusCode status,
                string? reason)
            {
                InnerPayload = innerPayload;
                Header = header;
                Status = status;
                Reason = reason;
            }

            /// <summary>Decrypted payload bytes (success only).</summary>
            public ReadOnlyMemory<byte>? InnerPayload { get; }

            /// <summary>SecurityHeader read from the wire.</summary>
            public UadpSecurityHeader? Header { get; }

            /// <summary>Final status code.</summary>
            public StatusCode Status { get; }

            /// <summary>Diagnostic reason (failure only).</summary>
            public string? Reason { get; }

            /// <summary>True when the unwrap succeeded.</summary>
            public bool IsSuccess => StatusCode.IsGood(Status);

            /// <summary>
            /// Builds a success result.
            /// </summary>
            public static UnwrapResult Success(
                ReadOnlyMemory<byte> innerPayload,
                UadpSecurityHeader header)
            {
                return new UnwrapResult(innerPayload, header, StatusCodes.Good, null);
            }

            /// <summary>
            /// Builds a failure result.
            /// </summary>
            /// <exception cref="ArgumentException"></exception>
            public static UnwrapResult Failure(StatusCode status, string reason)
            {
                if (string.IsNullOrEmpty(reason))
                {
                    throw new ArgumentException(
                        "Failure reason must be non-empty.",
                        nameof(reason));
                }
                return new UnwrapResult(null, null, status, reason);
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="UadpSecurityWrapper"/>.
    /// </summary>
    internal static partial class UadpSecurityWrapperLog
    {
        [LoggerMessage(EventId = PubSubEventIds.UadpSecurityWrapper + 0, Level = LogLevel.Debug,
            Message = "UadpSecurityWrapper wrapped message tokenId={TokenId} options={Options} " +
                "payload={PayloadLength} signed={SignedLength}")]
        public static partial void WrappedMessage(
            this ILogger logger,
            uint tokenId,
            UadpSecurityWrapOptions options,
            int payloadLength,
            int signedLength);

        [LoggerMessage(EventId = PubSubEventIds.UadpSecurityWrapper + 1, Level = LogLevel.Warning,
            Message = "UadpSecurityWrapper failed to parse SecurityHeader")]
        public static partial void FailedToParseSecurityHeader(this ILogger logger);

        [LoggerMessage(EventId = PubSubEventIds.UadpSecurityWrapper + 2, Level = LogLevel.Warning,
            Message = "UadpSecurityWrapper rejected unknown tokenId={TokenId}")]
        public static partial void RejectedUnknownToken(this ILogger logger, uint tokenId);

        [LoggerMessage(EventId = PubSubEventIds.UadpSecurityWrapper + 3, Level = LogLevel.Warning,
            Message = "UadpSecurityWrapper signature verification failed tokenId={TokenId}")]
        public static partial void SignatureVerificationFailed(this ILogger logger, uint tokenId);

        [LoggerMessage(EventId = PubSubEventIds.UadpSecurityWrapper + 4, Level = LogLevel.Warning,
            Message = "UadpSecurityWrapper rejected replay or nonce reuse tokenId={TokenId} " +
                "sequenceNumber={SequenceNumber}")]
        public static partial void RejectedReplayOrNonceReuse(
            this ILogger logger,
            uint tokenId,
            ulong sequenceNumber);
    }

}
