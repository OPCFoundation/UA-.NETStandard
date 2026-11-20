// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Creates the key pair behind a new application instance certificate.
    /// </summary>
    /// <remarks>
    /// This is the seam a device uses to have its key generated inside a TPM, an
    /// HSM or a secure element rather than in software. The builder arrives with
    /// the subject, subject alternative names and lifetime already set, so an
    /// implementation only decides where the key comes from and how the
    /// certificate is signed.
    /// <para>
    /// An implementation backed by hardware cannot call the parameterless
    /// <c>CreateForRSA</c> or <c>CreateForECDsa</c>, because those generate a key
    /// in software. It must supply the public key it generated in the device with
    /// <c>SetRSAPublicKey</c> or <c>SetECDsaPublicKey</c> and sign with an
    /// <c>X509SignatureGenerator</c> that calls back into the device. Note also
    /// that <c>CertificateRequest.CreateSelfSigned</c> cannot be used with a non
    /// extractable key, because it attaches the key with
    /// <c>X509Certificate2.CopyWithPrivateKey</c>.
    /// </para>
    /// </remarks>
    public interface IKeyPairGenerator
    {
        /// <summary>
        /// Completes a configured builder into a certificate.
        /// </summary>
        /// <param name="builder">
        /// A builder with the subject, names and lifetime already set.
        /// </param>
        /// <param name="certificateType">
        /// The certificate type, which decides whether an RSA or an elliptic
        /// curve key is required.
        /// </param>
        /// <param name="keySizeInBits">
        /// The RSA key size. Ignored for elliptic curve certificate types, whose
        /// size follows from the curve.
        /// </param>
        /// <returns>The new certificate, including its private key.</returns>
        Certificate CreateCertificate(
            ICertificateBuilder builder,
            NodeId certificateType,
            ushort keySizeInBits);
    }
}
