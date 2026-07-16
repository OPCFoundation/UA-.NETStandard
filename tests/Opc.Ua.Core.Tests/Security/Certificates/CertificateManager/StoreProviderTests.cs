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

using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    [TestFixture]
    [Category("StoreProvider")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class StoreProviderTests
    {
        [Test]
        public void DirectoryStoreProviderSupportsDirectoryPath()
        {
            var provider = new DirectoryStoreProvider();

            Assert.That(provider.SupportsStorePath(@"C:\MyCerts"), Is.True);
            Assert.That(provider.StoreTypeName, Is.EqualTo(CertificateStoreType.Directory));
        }

        [Test]
        public void DirectoryStoreProviderDoesNotSupportX509StorePath()
        {
            var provider = new DirectoryStoreProvider();

            Assert.That(provider.SupportsStorePath("X509Store:CurrentUser\\My"), Is.False);
        }

        [Test]
        public void X509StoreProviderSupportsX509StorePath()
        {
            var provider = new X509StoreProvider();

            Assert.That(provider.SupportsStorePath("X509Store:CurrentUser\\My"), Is.True);
            Assert.That(provider.StoreTypeName, Is.EqualTo(CertificateStoreType.X509Store));
        }

        [Test]
        public void X509StoreProviderDoesNotSupportDirectoryPath()
        {
            var provider = new X509StoreProvider();

            Assert.That(provider.SupportsStorePath(@"C:\MyCerts"), Is.False);
        }

        [Test]
        public void InMemoryStoreProviderSupportsInMemoryPath()
        {
            var provider = new InMemoryStoreProvider();

            Assert.That(provider.SupportsStorePath("InMemory:TestStore"), Is.True);
            Assert.That(provider.StoreTypeName, Is.EqualTo("InMemory"));
        }

        [Test]
        public void DirectoryStoreProviderCreatesStore()
        {
            var provider = new DirectoryStoreProvider();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            using ICertificateStore store = provider.CreateStore(telemetry);

            Assert.That(store, Is.Not.Null);
            Assert.That(store, Is.InstanceOf<DirectoryCertificateStore>());
        }
    }
}
