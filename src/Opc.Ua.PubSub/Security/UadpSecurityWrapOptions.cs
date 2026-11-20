/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// Selects the combination of signing and encryption applied by
    /// <see cref="UadpSecurityWrapper.WrapAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three combinations match the spec-defined SecurityFlags
    /// (<c>NetworkMessageSigned</c> and <c>NetworkMessageEncrypted</c>)
    /// permitted by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.2.5">
    /// Part 14 Annex A.2.2.5</see>. In <see cref="SignOnly"/> the
    /// security footer remains empty and no encryption is applied; in
    /// <see cref="SignAndEncrypt"/> the payload is encrypted and the
    /// signature covers prefix, header and ciphertext per Annex
    /// A.2.1.6.
    /// </para>
    /// </remarks>
    public enum UadpSecurityWrapOptions
    {
        /// <summary>
        /// Append a signature over the prefix, security header and
        /// cleartext payload; do not encrypt. Matches
        /// <see cref="MessageSecurityMode.Sign"/>.
        /// </summary>
        SignOnly,

        /// <summary>
        /// Encrypt the payload only; do not append a signature. Rarely
        /// used in practice but supported by the wire format.
        /// </summary>
        EncryptOnly,

        /// <summary>
        /// Encrypt the payload and append a signature. Default mode;
        /// matches <see cref="MessageSecurityMode.SignAndEncrypt"/>.
        /// </summary>
        SignAndEncrypt
    }
}
