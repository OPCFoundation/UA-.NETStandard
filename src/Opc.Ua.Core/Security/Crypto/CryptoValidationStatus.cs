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
    /// How much is known about the validation status of the cryptographic module
    /// a provider uses.
    /// </summary>
    /// <remarks>
    /// .NET holds no cryptographic module validation certificate of its own; it
    /// calls through to the module the operating system supplies. The distinction
    /// that matters in practice is therefore not "validated or not" but whether
    /// the provider can name a certificate, whether it merely inherits whatever
    /// the platform was configured with, or whether it is something else
    /// entirely.
    /// </remarks>
    public enum CryptoValidationLevel
    {
        /// <summary>
        /// The provider did not say. Treated as <see cref="Uncertified"/> when
        /// filtering, and always reported when auditing.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// The provider performs cryptography that carries no validation
        /// certificate, for example a third party library or a bespoke
        /// implementation.
        /// </summary>
        Uncertified = 1,

        /// <summary>
        /// The provider defers to platform cryptography, which is validated when
        /// the operating system is configured for it. This is the honest status
        /// of the default provider: whether the module is actually running in a
        /// validated mode is a deployment property, not a property of this stack.
        /// </summary>
        FipsCapablePlatform = 2,

        /// <summary>
        /// The provider asserts a specific validation certificate for the module
        /// it uses, named in <see cref="CryptoValidationStatus.CertificateReference"/>.
        /// </summary>
        FipsValidated = 3
    }

    /// <summary>
    /// Describes the provenance of the cryptographic module behind a provider.
    /// </summary>
    /// <param name="Level">How much is known about the module's validation.</param>
    /// <param name="ModuleName">
    /// The module performing the cryptography, for example
    /// <c>Windows CNG bcryptprimitives.dll</c>. May be <c>null</c>.
    /// </param>
    /// <param name="CertificateReference">
    /// The validation certificate, for example <c>CMVP #4825</c>. May be
    /// <c>null</c> when <paramref name="Level"/> is not
    /// <see cref="CryptoValidationLevel.FipsValidated"/>.
    /// </param>
    /// <remarks>
    /// This is what makes the use of uncertified cryptography auditable: a
    /// provider states what it is, and the stack can report and, when configured
    /// to, refuse it.
    /// </remarks>
    public readonly record struct CryptoValidationStatus(
        CryptoValidationLevel Level,
        string? ModuleName = null,
        string? CertificateReference = null)
    {
        /// <summary>
        /// The status of a provider that defers to platform cryptography.
        /// </summary>
        public static CryptoValidationStatus Platform { get; } = new(
            CryptoValidationLevel.FipsCapablePlatform,
            ".NET platform cryptography");

        /// <summary>
        /// Whether the provider may be used when only validated cryptography is
        /// permitted.
        /// </summary>
        public bool IsAcceptableForFips
            => Level is CryptoValidationLevel.FipsValidated
                or CryptoValidationLevel.FipsCapablePlatform;

        /// <inheritdoc/>
        public override string ToString()
        {
            if (CertificateReference != null)
            {
                return $"{Level} ({ModuleName}, {CertificateReference})";
            }

            return ModuleName != null ? $"{Level} ({ModuleName})" : Level.ToString();
        }
    }
}
