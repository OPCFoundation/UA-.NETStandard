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
    /// The default provider, which defers every operation to the cryptography
    /// the .NET platform supplies.
    /// </summary>
    /// <remarks>
    /// This provider claims every purpose without narrowing to a policy or
    /// certificate type, which reproduces today's behaviour exactly: whatever the
    /// platform supports is available, and nothing is filtered out. It reports
    /// <see cref="CryptoValidationLevel.FipsCapablePlatform"/> rather than
    /// claiming validation, because whether the underlying module is running in a
    /// validated mode is a property of how the machine is configured and not
    /// something this stack can assert.
    /// </remarks>
    public sealed class PlatformCryptoProvider : ICryptoProvider
    {
        /// <summary>
        /// The shared instance.
        /// </summary>
        public static PlatformCryptoProvider Instance { get; } = new();

        /// <inheritdoc/>
        public string Name => "Platform";

        /// <inheritdoc/>
        public CryptoValidationStatus Validation => CryptoValidationStatus.Platform;

        /// <inheritdoc/>
        public ArrayOf<CryptoCapability> Capabilities => s_capabilities;

        private PlatformCryptoProvider()
        {
        }

        private static readonly ArrayOf<CryptoCapability> s_capabilities = new(
            new CryptoCapability[]
            {
                new(CryptoPurpose.ApplicationInstanceKey),
                new(CryptoPurpose.UserIdentityKey),
                new(CryptoPurpose.KeyAgreement),
                new(CryptoPurpose.CertificateIssuance),
                new(CryptoPurpose.ChannelSymmetric),
                new(CryptoPurpose.RandomNumberGeneration)
            });
    }
}
