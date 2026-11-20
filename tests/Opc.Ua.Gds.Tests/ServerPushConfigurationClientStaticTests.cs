/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
                Is.EqualTo(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType));
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
                    Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType
            };

            Assert.That(
                client.ApplicationCertificateType,
                Is.EqualTo(Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType));
        }
    }
}
