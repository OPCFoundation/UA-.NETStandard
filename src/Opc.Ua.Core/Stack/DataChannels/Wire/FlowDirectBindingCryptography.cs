/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Security.Cryptography;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.DataChannels.Wire
{
    /// <summary>
    /// Holds the locally authenticated values used to derive a direct-binding transcript.
    /// </summary>
    /// <param name="TransportProfileUri">The selected transport profile URI.</param>
    /// <param name="Capability">The selected direct-delivery capability.</param>
    /// <param name="ContextId">The confirmed Data Channel context identifier.</param>
    /// <param name="Generation">The nonzero context generation.</param>
    /// <param name="InitiatorApplicationUri">The authenticated initiator application URI.</param>
    /// <param name="AcceptorApplicationUri">The authenticated acceptor application URI.</param>
    /// <param name="InitiatorCertificateThumbprint">The initiator certificate SHA-256 thumbprint.</param>
    /// <param name="AcceptorCertificateThumbprint">The acceptor certificate SHA-256 thumbprint.</param>
    /// <param name="InitiatorChallenge">The fresh initiator HELLO challenge.</param>
    /// <param name="AcceptorChallenge">The fresh acceptor HELLO challenge.</param>
    /// <param name="Authority">The exact selected endpoint authority.</param>
    /// <param name="Path">The exact selected endpoint path and query.</param>
    internal readonly record struct FlowBindingTranscript(
        string TransportProfileUri,
        string Capability,
        Guid ContextId,
        ulong Generation,
        string InitiatorApplicationUri,
        string AcceptorApplicationUri,
        ByteString InitiatorCertificateThumbprint,
        ByteString AcceptorCertificateThumbprint,
        ByteString InitiatorChallenge,
        ByteString AcceptorChallenge,
        string Authority,
        string Path);

    /// <summary>
    /// Derives the Part 31/32 exporter transcript digest and role-specific proofs.
    /// </summary>
    internal static class FlowDirectBindingCryptography
    {
        /// <summary>
        /// The maximum canonical transcript size for its seven bounded String fields.
        /// </summary>
        private const int MaximumTranscriptBytes =
            7 * (sizeof(uint) + FlowWireText.MaxUriBytes) +
            sizeof(ulong) + 16 + 4 * (sizeof(uint) + 32);

        /// <summary>
        /// Creates SHA-256 over the canonical Part 6 transcript in initiator/acceptor order.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static ByteString ComputeTranscriptDigest(in FlowBindingTranscript transcript)
        {
            Validate(transcript);
            var counter = new FlowBinaryWriter([], MaximumTranscriptBytes);
            WriteTranscript(ref counter, in transcript);
            byte[] bytes = new byte[counter.Length];
            var writer = new FlowBinaryWriter(bytes, bytes.Length);
            WriteTranscript(ref writer, in transcript);
            try
            {
                return new ByteString(SHA256.HashData(bytes));
            }
            finally
            {
                CryptoUtils.ZeroMemory(bytes);
            }
        }

        /// <summary>
        /// Creates the role-specific HMAC-SHA-256 proof over the transcript digest.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static ByteString CreateProof(ByteString exporter, byte role, ByteString digest)
        {
            ValidateFixed(exporter);
            ValidateFixed(digest);
            if (role > 1)
            {
                throw new FlowCodecException(FlowCodecError.InvalidEnumeration);
            }

            byte[] key = exporter.Span.ToArray();
            byte[] message = new byte[ProofRoleLength(role) + digest.Length];
            try
            {
                int roleLength = WriteProofRole(role, message);
                digest.Span.CopyTo(message.AsSpan(roleLength));
                using var hmac = new HMACSHA256(key);
                return new ByteString(hmac.ComputeHash(message));
            }
            finally
            {
                CryptoUtils.ZeroMemory(key);
                CryptoUtils.ZeroMemory(message);
            }
        }

        /// <summary>
        /// Checks a received proof without a data-dependent byte comparison.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static bool VerifyProof(ByteString exporter, byte role, ByteString digest, ByteString proof)
        {
            ValidateFixed(proof);
            ByteString expected = CreateProof(exporter, role, digest);
            return CryptoUtils.FixedTimeEquals(expected.Span, proof.Span);
        }

        /// <summary>
        /// Validates bounded, nonempty strings and fixed-size context values before allocating.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static void Validate(in FlowBindingTranscript transcript)
        {
            if (transcript.Generation == 0)
            {
                throw new FlowCodecException(FlowCodecError.OutOfRange);
            }
            ValidateText(transcript.TransportProfileUri, FlowWireText.MaxUriBytes);
            ValidateText(transcript.Capability, FlowWireText.MaxContentTypeBytes);
            ValidateText(transcript.InitiatorApplicationUri, FlowWireText.MaxUriBytes);
            ValidateText(transcript.AcceptorApplicationUri, FlowWireText.MaxUriBytes);
            ValidateText(transcript.Authority, FlowWireText.MaxUriBytes);
            ValidateText(transcript.Path, FlowWireText.MaxUriBytes);
            ValidateFixed(transcript.InitiatorCertificateThumbprint);
            ValidateFixed(transcript.AcceptorCertificateThumbprint);
            ValidateFixed(transcript.InitiatorChallenge);
            ValidateFixed(transcript.AcceptorChallenge);
        }

        /// <summary>
        /// Validates one required canonical String without normalizing its contents.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static void ValidateText(string value, int maximumBytes)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new FlowCodecException(FlowCodecError.InvalidLength);
            }
            if (FlowWireText.Utf8Length(value) > maximumBytes)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
        }

        /// <summary>
        /// Validates one exact 32-byte binding value.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static void ValidateFixed(ByteString value)
        {
            if (value.IsNull || value.Length != 32)
            {
                throw new FlowCodecException(FlowCodecError.InvalidChallenge);
            }
        }

        /// <summary>
        /// Writes every transcript field in the order specified by the shared transport contract.
        /// </summary>
        private static void WriteTranscript(ref FlowBinaryWriter writer, in FlowBindingTranscript transcript)
        {
            writer.String("OPCUA-DIRECT-BINDING-1");
            writer.String(transcript.TransportProfileUri);
            writer.String(transcript.Capability);
            writer.Guid(transcript.ContextId);
            writer.UInt64(transcript.Generation);
            writer.String(transcript.InitiatorApplicationUri);
            writer.String(transcript.AcceptorApplicationUri);
            writer.Challenge(transcript.InitiatorCertificateThumbprint);
            writer.Challenge(transcript.AcceptorCertificateThumbprint);
            writer.Challenge(transcript.InitiatorChallenge);
            writer.Challenge(transcript.AcceptorChallenge);
            writer.String(transcript.Authority);
            writer.String(transcript.Path);
        }

        /// <summary>
        /// Gets the exact ASCII role label size.
        /// </summary>
        private static int ProofRoleLength(byte role)
        {
            return role == 0 ? 9 : 8;
        }

        /// <summary>
        /// Writes the exact ASCII role label and returns its length.
        /// </summary>
        private static int WriteProofRole(byte role, Span<byte> destination)
        {
            ReadOnlySpan<byte> value = role == 0 ? "initiator"u8 : "acceptor"u8;
            value.CopyTo(destination);
            return value.Length;
        }
    }
}
