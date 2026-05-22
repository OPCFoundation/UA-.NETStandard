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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Conformance.Tests.Security
{
    /// <summary>
    /// Helper to build an <see cref="UserIdentity"/> from a raw
    /// <see cref="X509Certificate2"/> or <see cref="Certificate"/>.
    /// </summary>
    /// <remarks>
    /// In v1.6 the legacy <c>new UserIdentity(X509Certificate2)</c> and
    /// <c>new UserIdentity(Certificate)</c> constructors were removed in
    /// favour of provider-based <see cref="UserIdentity.CreateAsync"/>.
    /// These conformance tests still hold transient X509 user certs
    /// (created on-the-fly, not registered with a store / provider),
    /// so we construct an <see cref="X509IdentityToken"/> wire type
    /// directly and pass it to the surviving
    /// <see cref="UserIdentity(UserIdentityToken)"/> constructor.
    /// </remarks>
    internal static class X509UserIdentityHelper
    {
        public static UserIdentity Create(X509Certificate2 certificate)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }
            return Create(certificate.RawData);
        }

        public static UserIdentity Create(Certificate certificate)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }
            return Create(certificate.RawData);
        }

        private static UserIdentity Create(byte[] rawData)
        {
            var token = new X509IdentityToken
            {
                CertificateData = (ByteString)rawData
            };
            return new UserIdentity(token);
        }
    }
}
