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
