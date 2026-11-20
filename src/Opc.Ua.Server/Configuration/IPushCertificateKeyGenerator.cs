/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Generates the temporary server application key pair and self-signed
    /// certificate used by <c>CreateSigningRequest</c> when a new private key
    /// is requested (OPC 10000-12 §7.10.10).
    /// </summary>
    /// <remarks>
    /// <para>
    /// §7.10.10 requires the caller to supply a <c>Nonce</c> of at least
    /// 32 bytes as <b>additional entropy</b> when a new private key is
    /// generated. Implementations shall genuinely incorporate that entropy
    /// into the generated private key (mixed with a server-side cryptographic
    /// random source) rather than merely validating its length.
    /// </para>
    /// <para>
    /// The abstraction is resolved through the server's dependency-injection
    /// container so hosts can replace it (for example, to delegate to a
    /// hardware security module). When no implementation is registered,
    /// <see cref="AdditionalEntropyCertificateKeyGenerator"/> is used as the
    /// direct-construction fallback.
    /// </para>
    /// </remarks>
    public interface IPushCertificateKeyGenerator
    {
        /// <summary>
        /// Creates a new self-signed application certificate whose private
        /// key incorporates the caller-supplied additional entropy.
        /// </summary>
        /// <param name="request">
        /// The certificate parameters, including the additional entropy
        /// (<see cref="PushCertificateKeyGenerationRequest.AdditionalEntropy"/>).
        /// </param>
        /// <param name="cancellationToken">A token to cancel a slow key generation.</param>
        /// <returns>
        /// A new <see cref="Certificate"/> containing the generated private
        /// key. The caller owns and must dispose the returned certificate.
        /// </returns>
        Certificate CreateApplicationCertificate(
            PushCertificateKeyGenerationRequest request,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Parameters for <see cref="IPushCertificateKeyGenerator.CreateApplicationCertificate"/>.
    /// </summary>
    public sealed class PushCertificateKeyGenerationRequest
    {
        /// <summary>
        /// The OPC UA certificate type the new certificate is created for.
        /// Determines the key algorithm (RSA vs ECC) and, for ECC, the curve.
        /// </summary>
        public required NodeId CertificateTypeId { get; init; }

        /// <summary>
        /// The application URI placed into the certificate's
        /// <c>subjectAltName</c> extension.
        /// </summary>
        public required string ApplicationUri { get; init; }

        /// <summary>
        /// The human-readable application name.
        /// </summary>
        public required string ApplicationName { get; init; }

        /// <summary>
        /// The subject distinguished name of the new certificate.
        /// </summary>
        public required string SubjectName { get; init; }

        /// <summary>
        /// The DNS names and IP addresses placed into the certificate's
        /// <c>subjectAltName</c> extension.
        /// </summary>
        public ArrayOf<string> DomainNames { get; init; }

        /// <summary>
        /// The RSA key size in bits. Ignored for ECC certificate types.
        /// A value of 0 selects the implementation default.
        /// </summary>
        public ushort KeySizeInBits { get; init; }

        /// <summary>
        /// The inclusive UTC start of the certificate validity period.
        /// </summary>
        public required DateTime NotBefore { get; init; }

        /// <summary>
        /// The exclusive UTC end of the certificate validity period.
        /// </summary>
        public required DateTime NotAfter { get; init; }

        /// <summary>
        /// Additional caller-supplied entropy (the §7.10.10 <c>Nonce</c>).
        /// Must already have been validated to be at least 32 bytes by the
        /// caller. Genuinely mixed into the generated private key.
        /// </summary>
        public ByteString AdditionalEntropy { get; init; }
    }
}
