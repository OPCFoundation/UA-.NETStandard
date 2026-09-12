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

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    [TestFixture]
    [Category("CertificateManager")]
    [NonParallelizable]
    public sealed class CertificateCoreLifetimeRegressionTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task RetiringCoreKeepsBorrowedStoresAliveUntilValidationFinishesAsync(bool dispose, bool cancel)
        {
            string path = Path.Combine(Path.GetTempPath(), "CertificateCoreBorrow-" + Guid.NewGuid().ToString("N"));
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var manager = new CertificateManager(telemetry, maxRejectedCertificates: -1);
            using Certificate certificate = CertificateBuilder.Create("CN=Core Borrow").CreateForRSA();
            using var request = new CancellationTokenSource();
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? disposal = null;
            try
            {
                manager.RegisterTrustList(TrustListIdentifier.Peers, path);
                using ICertificateStore seed = manager.OpenTrustedStore(TrustListIdentifier.Peers);
                await seed.AddAsync(certificate).ConfigureAwait(false);
                CertificateValidationResult warm = await manager.ValidateAsync(certificate).ConfigureAwait(false);
                Assert.That(warm.IsValid, Is.True);
                var core = (CertificateValidationCore)typeof(CertificateManager)
                    .GetField("m_peerCore", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
                var stores = (ConcurrentDictionary<CertificateStoreIdentifier, ICertificateStore>)
                    typeof(CertificateValidationCore).GetField("m_stores", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(core)!;
                KeyValuePair<CertificateStoreIdentifier, ICertificateStore> cached = stores.Single();
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var borrowedStore = new Mock<ICertificateStore>();
                int disposed = 0;
                borrowedStore.Setup(store => store.Dispose()).Callback(() =>
                {
                    Interlocked.Increment(ref disposed);
                    cached.Value.Dispose();
                });
                borrowedStore.Setup(store => store.FindByThumbprintAsync(
                        It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns(async (string thumbprint, CancellationToken ct) =>
                    {
                        CertificateCollection snapshot = await cached.Value.FindByThumbprintAsync(thumbprint, ct)
                            .ConfigureAwait(false);
                        bool returned = false;
                        try
                        {
                            entered.TrySetResult(true);
                            await release.Task.WaitAsync(ct).ConfigureAwait(false);
                            if (Volatile.Read(ref disposed) != 0)
                            {
                                throw new ObjectDisposedException("borrowed certificate store");
                            }
                            returned = true;
                            return snapshot;
                        }
                        finally
                        {
                            if (!returned)
                            {
                                snapshot.Dispose();
                            }
                        }
                    });
                stores[cached.Key] = borrowedStore.Object;
                Task<CertificateValidationResult> pending = manager.ValidateAsync(certificate, ct: request.Token);
                int disposedWhileBorrowed;
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    if (dispose)
                    {
                        disposal = manager.DisposeAsync().AsTask();
                    }
                    else
                    {
                        await seed.DeleteAsync(certificate.Thumbprint).ConfigureAwait(false);
                        manager.NotifyTrustListChanged(TrustListIdentifier.Peers, trustChanged: true, crlChanged: false);
                        CertificateValidationResult fresh = await manager.ValidateAsync(certificate).ConfigureAwait(false);
                        Assert.That(fresh.IsValid, Is.False);
                        Assert.That(fresh.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
                    }
                    disposedWhileBorrowed = Volatile.Read(ref disposed);
                    if (cancel)
                    {
                        request.Cancel();
                    }
                }
                finally
                {
                    release.TrySetResult(true);
                }
                if (cancel)
                {
                    Assert.CatchAsync<OperationCanceledException>(async () => await pending.ConfigureAwait(false));
                }
                else
                {
                    Assert.That((await pending.ConfigureAwait(false)).IsValid, Is.True);
                }
                if (disposal != null)
                {
                    await disposal.ConfigureAwait(false);
                }
                Assert.That(disposedWhileBorrowed, Is.Zero);
                borrowedStore.Verify(store => store.Dispose(), Times.Once);
            }
            finally
            {
                release.TrySetResult(true);
                if (disposal != null)
                {
                    await disposal.ConfigureAwait(false);
                }
                await manager.DisposeAsync().ConfigureAwait(false);
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }

        [Test]
        public async Task TrustListNamesRemainAStableSnapshotDuringConcurrentRegistrationAsync()
        {
            using var manager = new CertificateManager(NUnitTelemetryContext.Create());
            manager.RegisterTrustList(TrustListIdentifier.Peers, "snapshot-peers");
            IReadOnlyCollection<TrustListIdentifier> snapshot = manager.TrustLists;
            using IEnumerator<TrustListIdentifier> enumerator = snapshot.GetEnumerator();
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(TrustListIdentifier.Peers));
            await Task.Run(() => manager.RegisterTrustList(TrustListIdentifier.Users, "snapshot-users"))
                .ConfigureAwait(false);
            Assert.That(enumerator.MoveNext(), Is.False);
            Assert.That(snapshot, Has.Count.EqualTo(1));
            Assert.That(manager.TrustLists, Is.EquivalentTo(s_registered));
        }

        private static readonly TrustListIdentifier[] s_registered =
            [TrustListIdentifier.Peers, TrustListIdentifier.Users];
    }
}
