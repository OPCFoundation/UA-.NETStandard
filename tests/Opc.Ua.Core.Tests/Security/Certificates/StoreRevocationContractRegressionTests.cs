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

using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using Opc.Ua.X509StoreExtensions;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    [TestFixture]
    [Category("CertificateStore")]
    [NonParallelizable]
    public sealed class StoreRevocationContractRegressionTests
    {
        [Test]
        public async Task UnsupportedOsRevocationReturnsTheCapabilityStatusAsync()
        {
            using Certificate certificate = CertificateBuilder.Create("CN=Revocation Capability")
                .SetRSAKeySize(2048).CreateForRSA();
            using var store = new X509CertificateStore(NUnitTelemetryContext.Create());
            FieldInfo capability = typeof(PlatformHelper).GetField(
                "s_isWindowsWithCrlSupport", BindingFlags.NonPublic | BindingFlags.Static)!;
            bool? previous = (bool?)capability.GetValue(null);
            capability.SetValue(null, false);
            try
            {
                Assert.That(store.SupportsCRLs, Is.False);
                StatusCode status = await store.IsRevokedAsync(certificate, certificate).ConfigureAwait(false);
                Assert.That(status, Is.EqualTo(StatusCodes.BadNotSupported));
            }
            finally
            {
                capability.SetValue(null, previous);
            }
        }
    }
}
