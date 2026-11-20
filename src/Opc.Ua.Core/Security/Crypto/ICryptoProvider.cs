// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua
{
    /// <summary>
    /// A source of cryptographic capability, which may be the platform, another
    /// library, a hardware token or a remote key service.
    /// </summary>
    /// <remarks>
    /// A provider does not perform the operations itself. The asymmetric
    /// operations the stack needs are already expressed by
    /// <see cref="System.Security.Cryptography.RSA"/> and
    /// <see cref="System.Security.Cryptography.ECDsa"/>, which are abstract, and
    /// hardware and cloud implementations of them already exist. Introducing a
    /// competing interface for signing and decryption would make those unusable.
    /// <para>
    /// What a provider supplies instead is the part the platform does not model:
    /// which capabilities it can serve, and what may be said about the module
    /// behind it. Those two facts drive selection, the advertised security
    /// policy set, and the audit trail.
    /// </para>
    /// </remarks>
    public interface ICryptoProvider
    {
        /// <summary>
        /// A stable identifier used in configuration, logs, metrics and the
        /// address space, for example <c>Platform</c> or <c>TPM2.0-CNG</c>.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// What may be said about the validation status of the module behind
        /// this provider.
        /// </summary>
        CryptoValidationStatus Validation { get; }

        /// <summary>
        /// The capabilities this provider can serve.
        /// </summary>
        ArrayOf<CryptoCapability> Capabilities { get; }
    }
}
