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
 *
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

using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Represents a single resolved entry in a certificate chain — a
    /// concrete <see cref="Certificate"/> plus the
    /// <see cref="CertificateValidationOptions"/> that govern its
    /// validation.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="ICertificateRegistry.GetIssuersAsync"/> and
    /// the certificate validator to carry chain elements without
    /// resorting to the conflated metadata-and-cert role of the legacy
    /// <see cref="CertificateIdentifier"/> wrapper. Returned references
    /// are caller-owned — callers must dispose <see cref="Certificate"/>
    /// when they are done with it.
    /// </remarks>
    /// <param name="Certificate">
    /// The certificate at this position in the chain. The caller owns
    /// this reference and is responsible for disposing it.
    /// </param>
    /// <param name="Options">
    /// Validation options associated with the source the certificate
    /// was loaded from (e.g. trusted-list-derived suppression flags).
    /// </param>
    public sealed record CertificateIssuerReference(
        Certificate Certificate,
        CertificateValidationOptions Options);
}
