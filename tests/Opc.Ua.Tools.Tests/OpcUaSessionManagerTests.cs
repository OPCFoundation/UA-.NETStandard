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

#if NET10_0
using System;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Tools.Tests
{
    [TestFixture]
    public class OpcUaSessionManagerTests
    {
        [Test]
        public void AutoAcceptCertificateScopeRestoresPreviousCallbackAfterFailure()
        {
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(static _ => { });
            using var certificateManager = new CertificateManager(telemetry);
            Func<Certificate, ServiceResult, bool> previousCallback = static (_, _) => false;
            certificateManager.AcceptError = previousCallback;

            Assert.That(
                () =>
                {
                    using IDisposable? scope =
                        OpcUaSessionManager.CreateAutoAcceptCertificateScope(
                            certificateManager,
                            true);
                    Assert.That(scope, Is.Not.Null);
                    Assert.That(certificateManager.AcceptError, Is.Not.SameAs(previousCallback));
                    throw new InvalidOperationException("Connection failed.");
                },
                Throws.TypeOf<InvalidOperationException>());

            Assert.That(certificateManager.AcceptError, Is.SameAs(previousCallback));
        }

        [Test]
        public void DisabledAutoAcceptCertificateScopeLeavesCallbackUnchanged()
        {
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(static _ => { });
            using var certificateManager = new CertificateManager(telemetry);
            Func<Certificate, ServiceResult, bool> previousCallback = static (_, _) => false;
            certificateManager.AcceptError = previousCallback;

            using IDisposable? scope = OpcUaSessionManager.CreateAutoAcceptCertificateScope(
                certificateManager,
                false);

            Assert.That(scope, Is.Null);
            Assert.That(certificateManager.AcceptError, Is.SameAs(previousCallback));
        }

        [Test]
        public void AutoAcceptCertificateScopeRestoresNullCallback()
        {
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(static _ => { });
            using var certificateManager = new CertificateManager(telemetry);
            Assert.That(certificateManager.AcceptError, Is.Null);

            using (IDisposable? scope = OpcUaSessionManager.CreateAutoAcceptCertificateScope(
                certificateManager,
                true))
            {
                Assert.That(scope, Is.Not.Null);
                Assert.That(certificateManager.AcceptError, Is.Not.Null);
            }

            Assert.That(certificateManager.AcceptError, Is.Null);
        }
    }
}
#endif
