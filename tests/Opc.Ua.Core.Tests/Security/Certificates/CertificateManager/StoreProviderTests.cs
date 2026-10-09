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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
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
            using var provider = new InMemoryStoreProvider();

            Assert.That(provider.SupportsStorePath("InMemory:TestStore"), Is.True);
            Assert.That(provider.StoreTypeName, Is.EqualTo("InMemory"));
        }

        /// <summary>
        /// Stores opened on the same InMemory path share their certificates, so a
        /// certificate written through one store instance (a trust list update)
        /// is seen by the next (the validator). Before the fix every open
        /// returned a fresh empty store.
        /// </summary>
        [Test]
        public async Task InMemoryStoreProviderSharesCertificatesPerPathAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Certificate certificate = CertificateBuilder
                .Create("CN=InMemory Store Test")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using var provider = new InMemoryStoreProvider();

            using (ICertificateStore writer = provider.CreateStore(telemetry))
            {
                writer.Open("InMemory:peers");
                await writer.AddAsync(certificate).ConfigureAwait(false);
            }

            using (ICertificateStore reader = provider.CreateStore(telemetry))
            {
                reader.Open("InMemory:Peers");
                using CertificateCollection found = await reader
                    .FindByThumbprintAsync(certificate.Thumbprint)
                    .ConfigureAwait(false);
                Assert.That(found, Has.Count.EqualTo(1));
                Assert.That(reader.StorePath, Is.EqualTo("InMemory:Peers"));
            }

            using (ICertificateStore other = provider.CreateStore(telemetry))
            {
                other.Open("InMemory:users");
                using CertificateCollection all = await other.EnumerateAsync().ConfigureAwait(false);
                Assert.That(all, Is.Empty);
            }
        }

        /// <summary>
        /// A store opened before the provider is disposed fails every later
        /// operation with the provider's ObjectDisposedException instead of
        /// touching its disposed backing store, and a store opened after the
        /// provider is disposed cannot create a new backing store nothing
        /// would ever release.
        /// </summary>
        [Test]
        public async Task InMemoryStoreProviderRejectsStoreUseAfterDisposeAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Certificate certificate = CertificateBuilder
                .Create("CN=InMemory Store Dispose Test")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            var provider = new InMemoryStoreProvider();
            using ICertificateStore store = provider.CreateStore(telemetry);
            store.Open("InMemory:peers");
            await store.AddAsync(certificate).ConfigureAwait(false);

            provider.Dispose();
            provider.Dispose();

            ObjectDisposedException enumerate = Assert.ThrowsAsync<ObjectDisposedException>(
                () => store.EnumerateAsync());
            Assert.That(enumerate.ObjectName, Is.EqualTo(nameof(InMemoryStoreProvider)));
            // CA2025 false positive: ThrowsAsync awaits the task before the
            // certificate goes out of scope.
#pragma warning disable CA2025
            ObjectDisposedException add = Assert.ThrowsAsync<ObjectDisposedException>(
                () => store.AddAsync(certificate));
#pragma warning restore CA2025
            Assert.That(add.ObjectName, Is.EqualTo(nameof(InMemoryStoreProvider)));
            ObjectDisposedException find = Assert.ThrowsAsync<ObjectDisposedException>(
                () => store.FindByThumbprintAsync(certificate.Thumbprint));
            Assert.That(find.ObjectName, Is.EqualTo(nameof(InMemoryStoreProvider)));

            using ICertificateStore late = provider.CreateStore(telemetry);
            Assert.Throws<ObjectDisposedException>(() => late.Open("InMemory:users"));
        }

        /// <summary>
        /// Disposing the provider while an open store is used concurrently either
        /// lets an operation complete on the intact backing store or fails it
        /// with ObjectDisposedException; it never runs against a store that is
        /// being disposed.
        /// </summary>
        [Test]
        public async Task InMemoryStoreProviderDisposeIsSerializedWithStoreOperationsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Certificate certificate = CertificateBuilder
                .Create("CN=InMemory Store Race Test")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            for (int iteration = 0; iteration < 20; iteration++)
            {
                var provider = new InMemoryStoreProvider();
                using ICertificateStore store = provider.CreateStore(telemetry);
                store.Open("InMemory:peers");
                await store.AddAsync(certificate).ConfigureAwait(false);

                var reader = Task.Run(async () =>
                {
                    while (true)
                    {
                        try
                        {
                            using CertificateCollection all = await store
                                .EnumerateAsync()
                                .ConfigureAwait(false);
                            Assert.That(all, Has.Count.EqualTo(1));
                        }
                        catch (ObjectDisposedException e)
                            when (e.ObjectName == nameof(InMemoryStoreProvider))
                        {
                            return;
                        }
                    }
                });

                provider.Dispose();
                await reader.ConfigureAwait(false);
            }
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
