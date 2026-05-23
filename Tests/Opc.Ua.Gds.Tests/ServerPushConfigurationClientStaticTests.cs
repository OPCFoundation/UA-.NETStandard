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

using NUnit.Framework;
using Opc.Ua.Gds.Client;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Unit tests for the surface of <see cref="ServerPushConfigurationClient"/>
    /// that does not require a live ServerConfiguration: in particular the
    /// caller-overridable <c>ApplicationCertificateType</c> default
    /// (OPC 10000-12 §7.10 - Track D quick-win).
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ServerPushConfigurationClientStaticTests
    {
        [Test]
        public void ApplicationCertificateTypeDefaultsToRsaSha256()
        {
            // Backwards-compat: the previous implementation hard-coded the
            // type to RsaSha256ApplicationCertificateType. The default must
            // remain that value so existing callers do not need to change.
            using var client = new ServerPushConfigurationClient(
                new ApplicationConfiguration());

            Assert.That(
                client.ApplicationCertificateType,
                Is.EqualTo(Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType));
        }

        [Test]
        public void ApplicationCertificateTypeIsCallerOverridable()
        {
            // Callers managing ECC, Brainpool or HTTPS certificates set
            // the type before invoking certificate-management methods
            // (Track D - d-push-client-multitype).
            using var client = new ServerPushConfigurationClient(
                new ApplicationConfiguration())
            {
                ApplicationCertificateType =
                    Opc.Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType
            };

            Assert.That(
                client.ApplicationCertificateType,
                Is.EqualTo(Opc.Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType));
        }
    }
}
