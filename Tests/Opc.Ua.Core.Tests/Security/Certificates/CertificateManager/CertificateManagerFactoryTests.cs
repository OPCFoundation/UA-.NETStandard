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

#pragma warning disable CA2007
#nullable enable

using System;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for <see cref="CertificateManagerFactory"/> and <see cref="CertificateManagerOptions"/>.
    /// </summary>
    [TestFixture]
    [Category("CertificateManager")]
    [Parallelizable]
    public class CertificateManagerFactoryTests
    {
        private ITelemetryContext m_telemetry = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            (m_telemetry as IDisposable)?.Dispose();
        }

        [Test]
        public void AddTrustListReturnsSameOptionsAndRegistersTrustList()
        {
            var store = new Mock<ICertificateStore>(MockBehavior.Strict);
            store.Setup(s => s.Open("mock-trusted", true));
            store.Setup(s => s.Dispose());

            var provider = new Mock<ICertificateStoreProvider>(MockBehavior.Strict);
            provider.SetupGet(p => p.StoreTypeName).Returns(CertificateStoreType.Directory);
            provider.Setup(p => p.CreateStore(m_telemetry)).Returns(store.Object);

            TrustListIdentifier trustList = new TrustListIdentifier("CustomTrustList");
            using CertificateManager manager = CertificateManagerFactory.Create(
                new SecurityConfiguration(),
                m_telemetry,
                options =>
                {
                    CertificateManagerOptions returned = options.AddTrustList(trustList.Name, "mock-trusted");
                    Assert.That(returned, Is.SameAs(options));
                    options.AddStoreProvider(provider.Object);
                });

            Assert.That(manager.TrustLists, Does.Contain(trustList));

            using ICertificateStore openedStore = manager.OpenTrustedStore(trustList);
            Assert.That(openedStore, Is.SameAs(store.Object));
            provider.Verify(p => p.CreateStore(m_telemetry), Times.Once);
            store.Verify(s => s.Open("mock-trusted", true), Times.Once);
        }

        [Test]
        public void AddStoreProviderRejectsNullProvider()
        {
            var options = new CertificateManagerOptions();

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => options.AddStoreProvider(null!))!;

            Assert.That(exception.ParamName, Is.EqualTo("provider"));
        }

        [Test]
        public void AddStoreProviderReturnsSameOptionsAndAddsProvider()
        {
            var options = new CertificateManagerOptions();
            var provider = new Mock<ICertificateStoreProvider>(MockBehavior.Strict);

            CertificateManagerOptions returned = options.AddStoreProvider(provider.Object);

            Assert.That(returned, Is.SameAs(options));
            Assert.That(options.StoreProviders, Has.Count.EqualTo(1));
            Assert.That(options.StoreProviders, Does.Contain(provider.Object));
        }

        [Test]
        public void CreateRejectsNullSecurityConfiguration()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => CertificateManagerFactory.Create(null!, m_telemetry))!;

            Assert.That(exception.ParamName, Is.EqualTo("securityConfiguration"));
        }

        [Test]
        public void CreateRejectsNullTelemetryContext()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => CertificateManagerFactory.Create(new SecurityConfiguration(), null!))!;

            Assert.That(exception.ParamName, Is.EqualTo("telemetry"));
        }
    }
}