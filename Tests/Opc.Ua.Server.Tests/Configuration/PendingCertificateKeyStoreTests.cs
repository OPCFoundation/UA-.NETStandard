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

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Exercises the <see cref="IPendingCertificateKeyStore"/> implementations
    /// used to persist a regenerated <c>CreateSigningRequest</c> private key
    /// (OPC 10000-12 §7.10.10): the certificate-store-backed production
    /// default <see cref="DirectoryPendingCertificateKeyStore"/> and the
    /// process-memory-only <see cref="InMemoryPendingCertificateKeyStore"/>
    /// used only for deterministic unit tests.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [Parallelizable]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class PendingCertificateKeyStoreTests
    {
        private ITelemetryContext m_telemetry;
        private string m_basePath;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_basePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "pks",
                Guid.NewGuid().ToString("N")[..8]);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_basePath))
            {
                Directory.Delete(m_basePath, true);
            }
        }

        [Test]
        public async Task DirectoryStoreSaveThenTryTakeRoundTripsTheCertificateAsync()
        {
            var store = new DirectoryPendingCertificateKeyStore();
            PendingCertificateKeyContext context = CreateContext();
            using Certificate original = CreateTestCertificateWithKey();

            bool saved = await store.SaveAsync(context, original, CancellationToken.None).ConfigureAwait(false);
            Assert.That(saved, Is.True);

            using Certificate taken = await store.TryTakeAsync(context, CancellationToken.None).ConfigureAwait(false);
            Assert.That(taken, Is.Not.Null);
            Assert.That(taken!.Thumbprint, Is.EqualTo(original.Thumbprint));
            Assert.That(taken.HasPrivateKey, Is.True);

            // TryTakeAsync consumes the entry.
            using Certificate takenAgain = await store.TryTakeAsync(context, CancellationToken.None).ConfigureAwait(false);
            Assert.That(takenAgain, Is.Null);
        }

        [Test]
        public async Task DirectoryStoreSaveTwiceReplacesThePreviousEntryAsync()
        {
            var store = new DirectoryPendingCertificateKeyStore();
            PendingCertificateKeyContext context = CreateContext();
            using Certificate first = CreateTestCertificateWithKey();
            using Certificate second = CreateTestCertificateWithKey();

            Assert.That(await store.SaveAsync(context, first, CancellationToken.None).ConfigureAwait(false), Is.True);
            Assert.That(await store.SaveAsync(context, second, CancellationToken.None).ConfigureAwait(false), Is.True);

            using Certificate taken = await store.TryTakeAsync(context, CancellationToken.None).ConfigureAwait(false);
            Assert.That(taken, Is.Not.Null);
            Assert.That(taken!.Thumbprint, Is.EqualTo(second.Thumbprint));

            using Certificate takenAgain = await store.TryTakeAsync(context, CancellationToken.None).ConfigureAwait(false);
            Assert.That(takenAgain, Is.Null, "only the most recent entry should remain");
        }

        [Test]
        public async Task DirectoryStoreRemoveDiscardsThePendingKeyAsync()
        {
            var store = new DirectoryPendingCertificateKeyStore();
            PendingCertificateKeyContext context = CreateContext();
            using Certificate original = CreateTestCertificateWithKey();

            Assert.That(await store.SaveAsync(context, original, CancellationToken.None).ConfigureAwait(false), Is.True);
            await store.RemoveAsync(context, CancellationToken.None).ConfigureAwait(false);

            using Certificate taken = await store.TryTakeAsync(context, CancellationToken.None).ConfigureAwait(false);
            Assert.That(taken, Is.Null);
        }

        [Test]
        public async Task DirectoryStoreClearsPasswordBuffersAfterSaveAndTakeAsync()
        {
            var store = new DirectoryPendingCertificateKeyStore();
            var passwordProvider = new TrackingPasswordProvider();
            PendingCertificateKeyContext context = CreateContext(passwordProvider: passwordProvider);
            using Certificate original = CreateTestCertificateWithKey();

            Assert.That(
                await store.SaveAsync(context, original, CancellationToken.None).ConfigureAwait(false),
                Is.True);
            Assert.That(passwordProvider.ReturnedPasswords, Has.Count.EqualTo(1));
            Assert.That(passwordProvider.ReturnedPasswords[0], Is.All.EqualTo('\0'));

            using Certificate taken = await store.TryTakeAsync(context, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(taken, Is.Not.Null);
            Assert.That(passwordProvider.ReturnedPasswords, Has.Count.EqualTo(2));
            Assert.That(passwordProvider.ReturnedPasswords[1], Is.All.EqualTo('\0'));
        }

        [Test]
        public async Task DirectoryStoreScopesEntriesByGroupAndTypeAsync()
        {
            var store = new DirectoryPendingCertificateKeyStore();
            PendingCertificateKeyContext contextA = CreateContext(
                certificateGroupId: new NodeId(Guid.NewGuid(), 1),
                certificateTypeId: new NodeId(Guid.NewGuid(), 1));
            PendingCertificateKeyContext contextB = CreateContext(
                certificateGroupId: new NodeId(Guid.NewGuid(), 1),
                certificateTypeId: new NodeId(Guid.NewGuid(), 1));
            using Certificate certA = CreateTestCertificateWithKey();

            Assert.That(await store.SaveAsync(contextA, certA, CancellationToken.None).ConfigureAwait(false), Is.True);

            using Certificate takenFromB = await store.TryTakeAsync(contextB, CancellationToken.None).ConfigureAwait(false);
            Assert.That(takenFromB, Is.Null, "a different (group, type) scope must not see another scope's pending key");

            using Certificate takenFromA = await store.TryTakeAsync(contextA, CancellationToken.None).ConfigureAwait(false);
            Assert.That(takenFromA, Is.Not.Null);
        }

        [Test]
        public async Task DirectoryStoreReturnsFalseForNonDirectoryStoreTypeAsync()
        {
            var store = new DirectoryPendingCertificateKeyStore();
            var context = new PendingCertificateKeyContext(
                new CertificateStoreIdentifier(@"CurrentUser\My", CertificateStoreType.X509Store),
                new NodeId(Guid.NewGuid(), 1),
                new NodeId(Guid.NewGuid(), 1),
                null,
                m_telemetry);
            using Certificate cert = CreateTestCertificateWithKey();

            bool saved = await store.SaveAsync(context, cert, CancellationToken.None).ConfigureAwait(false);

            Assert.That(saved, Is.False,
                "platform certificate stores do not support a dedicated pending-key sub-scope");
        }

        [Test]
        public async Task InMemoryStoreSaveThenTryTakeRoundTripsTheCertificateAsync()
        {
            var store = new InMemoryPendingCertificateKeyStore();
            PendingCertificateKeyContext context = CreateContext();
            using Certificate original = CreateTestCertificateWithKey();

            bool saved = await store.SaveAsync(context, original, CancellationToken.None).ConfigureAwait(false);
            Assert.That(saved, Is.True);

            using Certificate taken = await store.TryTakeAsync(context, CancellationToken.None).ConfigureAwait(false);
            Assert.That(taken, Is.Not.Null);
            Assert.That(taken!.Thumbprint, Is.EqualTo(original.Thumbprint));

            // The caller retains ownership of `original`; disposing it here
            // must not affect the store's own AddRef'd copy that was
            // already handed out above.
        }

        [Test]
        public async Task InMemoryStoreSaveTwiceReplacesAndDisposesThePreviousEntryAsync()
        {
            var store = new InMemoryPendingCertificateKeyStore();
            PendingCertificateKeyContext context = CreateContext();
            using Certificate first = CreateTestCertificateWithKey();
            using Certificate second = CreateTestCertificateWithKey();

            Assert.That(await store.SaveAsync(context, first, CancellationToken.None).ConfigureAwait(false), Is.True);
            Assert.That(await store.SaveAsync(context, second, CancellationToken.None).ConfigureAwait(false), Is.True);

            using Certificate taken = await store.TryTakeAsync(context, CancellationToken.None).ConfigureAwait(false);
            Assert.That(taken, Is.Not.Null);
            Assert.That(taken!.Thumbprint, Is.EqualTo(second.Thumbprint));
        }

        [Test]
        public async Task InMemoryStoreRemoveDisposesTheEntryAsync()
        {
            var store = new InMemoryPendingCertificateKeyStore();
            PendingCertificateKeyContext context = CreateContext();
            using Certificate original = CreateTestCertificateWithKey();

            Assert.That(await store.SaveAsync(context, original, CancellationToken.None).ConfigureAwait(false), Is.True);
            await store.RemoveAsync(context, CancellationToken.None).ConfigureAwait(false);

            using Certificate taken = await store.TryTakeAsync(context, CancellationToken.None).ConfigureAwait(false);
            Assert.That(taken, Is.Null);
        }

        [Test]
        public void InMemoryStoreSaveWithNullContextThrowsArgumentNullException()
        {
            var store = new InMemoryPendingCertificateKeyStore();
            using Certificate cert = CreateTestCertificateWithKey();

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await store.SaveAsync(null!, cert, CancellationToken.None).ConfigureAwait(false));
        }

        private PendingCertificateKeyContext CreateContext(
            NodeId certificateGroupId = default,
            NodeId certificateTypeId = default,
            ICertificatePasswordProvider passwordProvider = null)
        {
            return new PendingCertificateKeyContext(
                new CertificateStoreIdentifier(m_basePath, CertificateStoreType.Directory),
                certificateGroupId.IsNull ? new NodeId(Guid.NewGuid(), 1) : certificateGroupId,
                certificateTypeId.IsNull ? new NodeId(Guid.NewGuid(), 1) : certificateTypeId,
                passwordProvider,
                m_telemetry);
        }

        private static Certificate CreateTestCertificateWithKey()
        {
            return CertificateBuilder
                .Create("CN=PendingKey " + Guid.NewGuid().ToString("N")[..8])
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private sealed class TrackingPasswordProvider : ICertificatePasswordProvider
        {
            public System.Collections.Generic.List<char[]> ReturnedPasswords { get; } = [];

            public char[] GetPassword(CertificateIdentifier certificateIdentifier)
            {
                _ = certificateIdentifier;
                char[] password = "pending-key-password".ToCharArray();
                ReturnedPasswords.Add(password);
                return password;
            }
        }
    }
}
