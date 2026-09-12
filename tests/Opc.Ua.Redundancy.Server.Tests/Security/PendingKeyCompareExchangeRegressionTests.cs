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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Redundancy.Server.Tests.Security
{
    [TestFixture]
    public sealed class PendingKeyCompareExchangeRegressionTests
    {
        [Test]
        public async Task MismatchedUploadNeverClaimsTheSharedPendingKeyAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var writer = new SharedKeyValuePendingCertificateKeyStore(backend, new DistributedPushConfigurationOptions());
            var reader = new SharedKeyValuePendingCertificateKeyStore(backend, new DistributedPushConfigurationOptions());
            PendingCertificateKeyContext context = NewContext();
            using Certificate pending = NewKey("pending");
            using Certificate wrong = NewKey("wrong");
            Assert.That(await writer.SaveAsync(context, pending).ConfigureAwait(false), Is.True);
            using Certificate rejected = await reader.TryTakeMatchingAsync(context, wrong).ConfigureAwait(false);
            Assert.That(rejected, Is.Null);
            using Certificate taken = await reader.TryTakeMatchingAsync(context, pending).ConfigureAwait(false);
            Assert.That(taken.Thumbprint, Is.EqualTo(pending.Thumbprint));
            Assert.That(taken.HasPrivateKey, Is.True);
            using Certificate again = await writer.TryTakeMatchingAsync(context, pending).ConfigureAwait(false);
            Assert.That(again, Is.Null);
        }

        [Test]
        public async Task AConcurrentSigningRequestSurvivesBothSidesOfTheClaimCompareExchangeAsync(
            [Values(false, true)] bool replaceAfterClaim)
        {
            using var backend = new InMemorySharedKeyValueStore();
            var options = new DistributedPushConfigurationOptions();
            var writer = new SharedKeyValuePendingCertificateKeyStore(backend, options);
            PendingCertificateKeyContext context = NewContext();
            using Certificate original = NewKey("original");
            using Certificate newer = NewKey("newer");
            Assert.That(await writer.SaveAsync(context, original).ConfigureAwait(false), Is.True);
            var proxy = new Mock<ISharedKeyValueStore>();
            proxy.Setup(value => value.TryGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => backend.TryGetAsync(key, ct));
            proxy.Setup(value => value.CompareAndSwapAsync(
                    It.IsAny<string>(), It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((string key, ByteString expected, ByteString value, CancellationToken ct) =>
                    new ValueTask<bool>(SwapAsync(key, expected, value, ct)));
            var consumer = new SharedKeyValuePendingCertificateKeyStore(proxy.Object, options);
            using Certificate claimed = await consumer.TryTakeMatchingAsync(context, original).ConfigureAwait(false);
            Assert.That(claimed != null, Is.EqualTo(replaceAfterClaim));
            proxy.Verify(value => value.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            using Certificate retained = await writer.TryTakeMatchingAsync(context, newer).ConfigureAwait(false);
            Assert.That(retained, Is.Not.Null);
            Assert.That(retained.Thumbprint, Is.EqualTo(newer.Thumbprint));

            async Task<bool> SwapAsync(string key, ByteString expected, ByteString value, CancellationToken ct)
            {
                if (!replaceAfterClaim)
                {
                    Assert.That(await writer.SaveAsync(context, newer, ct).ConfigureAwait(false), Is.True);
                }
                bool swapped = await backend.CompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false);
                if (replaceAfterClaim)
                {
                    Assert.That(await writer.SaveAsync(context, newer, ct).ConfigureAwait(false), Is.True);
                }
                return swapped;
            }
        }

        [Test]
        public async Task ConditionalRestorationNeverReplacesANewerReplicaKeyAsync([Values(false, true)] bool replace)
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedKeyValuePendingCertificateKeyStore(backend, new DistributedPushConfigurationOptions());
            PendingCertificateKeyContext context = NewContext();
            using Certificate original = NewKey("original");
            using Certificate newer = NewKey("newer");
            Assert.That(await store.SaveAsync(context, original).ConfigureAwait(false), Is.True);
            using Certificate claimed = await store.TryTakeMatchingAsync(context, original).ConfigureAwait(false);
            if (replace)
            {
                Assert.That(await store.SaveAsync(context, newer).ConfigureAwait(false), Is.True);
            }
            Assert.That(await store.TryRestoreAsync(context, claimed).ConfigureAwait(false), Is.EqualTo(!replace));
            using Certificate retained = await store.TryTakeAsync(context).ConfigureAwait(false);
            Assert.That(retained.Thumbprint, Is.EqualTo(replace ? newer.Thumbprint : original.Thumbprint));
            Assert.That(retained.HasPrivateKey, Is.True);
        }

        [Test]
        public async Task CancelledMatchingClaimDoesNotChangeTheSharedRecordAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedKeyValuePendingCertificateKeyStore(backend, new DistributedPushConfigurationOptions());
            PendingCertificateKeyContext context = NewContext();
            using Certificate original = NewKey("original");
            Assert.That(await store.SaveAsync(context, original).ConfigureAwait(false), Is.True);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.That(() => store.TryTakeMatchingAsync(context, original, cancelled.Token).AsTask(),
                Throws.InstanceOf<OperationCanceledException>());
            using Certificate retained = await store.TryTakeAsync(context).ConfigureAwait(false);
            Assert.That(retained.Thumbprint, Is.EqualTo(original.Thumbprint));
        }

        private static Certificate NewKey(string name)
        {
            return DefaultCertificateFactory.Instance.CreateCertificate("CN=" + name).CreateForRSA();
        }

        private static PendingCertificateKeyContext NewContext()
        {
            return new PendingCertificateKeyContext(
                new CertificateStoreIdentifier(),
                new NodeId(Guid.NewGuid()),
                ObjectTypeIds.RsaSha256ApplicationCertificateType,
                null,
                NUnitTelemetryContext.Create());
        }
    }
}
