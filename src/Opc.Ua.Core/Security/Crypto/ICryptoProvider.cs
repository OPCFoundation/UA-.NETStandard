/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
