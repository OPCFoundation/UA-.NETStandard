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

using System;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Migration shim &#8212; restores the <c>CertificateIdentifier.Certificate</c>
    /// instance property that was removed in 1.6 because resolution now
    /// requires a registry/telemetry context. Accessing this shim member
    /// throws <see cref="NotSupportedException"/> at runtime; the
    /// <c>[Obsolete]</c> attribute and matching analyzer rule guide
    /// consumers to the async resolver replacement.
    /// </summary>
    public static class CertificateIdentifierShim
    {
        extension(CertificateIdentifier id)
        {
            /// <summary>
            /// Returns the resolved X.509 certificate. Removed in 1.6 because
            /// resolution is now async and requires a registry.
            /// </summary>
            [Obsolete("CertificateIdentifier.Certificate was removed in 1.6 because " +
                "resolution requires a registry/telemetry context. " +
                "Use CertificateIdentifierResolver.ResolveAsync(id, registry, needPrivateKey, applicationUri, telemetry, ct). " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/MigrationGuide.md#ua0018")]
            [OpcUaShim("UA0018")]
            public X509Certificate2? Certificate
                => throw new NotSupportedException(
                    "CertificateIdentifier.Certificate was removed in 1.6 because resolution " +
                    "requires a registry/telemetry context. Use " +
                    "CertificateIdentifierResolver.ResolveAsync(id, registry, needPrivateKey, applicationUri, telemetry, ct).");
        }
    }
}
