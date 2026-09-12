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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    [TestFixture]
    [Category("CertificateManager")]
    public sealed class CertificateExpirySnapshotRegressionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExpiryNotificationRetainsItsCertificateDuringReplacementOrDisposalAsync(bool dispose)
        {
            DateTime now = DateTime.UtcNow;
            var time = new FakeTimeProvider(new DateTimeOffset(now));
            using var manager = new CertificateManager(
                NUnitTelemetryContext.Create(), null, -1, TimeSpan.FromDays(14), time);
            using Certificate original = CertificateBuilder.Create("CN=Expiry Snapshot")
                .SetNotBefore(now.AddDays(-1)).SetNotAfter(now.AddDays(1)).CreateForRSA();
            using Certificate replacement = CertificateBuilder.Create("CN=Expiry Replacement")
                .SetNotBefore(now.AddDays(-1)).SetNotAfter(now.AddDays(90)).CreateForRSA();
            using var chain = new CertificateCollection();
            await manager.UpdateApplicationCertificateAsync(ObjectTypeIds.RsaSha256ApplicationCertificateType, original, chain)
                .ConfigureAwait(false);
            byte[] expected = original.RawData;
            original.Dispose();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            byte[]? observed = null;
            Exception? observationError = null;
            int notifications = 0;
            using IDisposable subscription = manager.CertificateChanges.Subscribe(new ExpiryObserver(change =>
            {
                if (change.Kind != CertificateChangeKind.CertificateExpiring)
                {
                    return;
                }
                Interlocked.Increment(ref notifications);
                entered.TrySetResult(true);
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("The certificate replacement barrier was not released.");
                }
                try
                {
                    using RSA key = change.OldCertificate!.GetRSAPublicKey()!;
                    Assert.That(key.KeySize, Is.EqualTo(2048));
                    observed = change.OldCertificate!.RawData;
                }
                catch (Exception ex)
                {
                    observationError = ex;
                }
            }));
            Task scan = Task.Run(() => time.Advance(TimeSpan.FromHours(1)));
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (dispose)
                {
                    await manager.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await manager.UpdateApplicationCertificateAsync(
                        ObjectTypeIds.RsaSha256ApplicationCertificateType, replacement, chain).ConfigureAwait(false);
                }
            }
            finally
            {
                release.Set();
                await scan.ConfigureAwait(false);
            }
            Assert.That(observationError, Is.Null);
            Assert.That(observed, Is.EqualTo(expected));
            time.Advance(TimeSpan.FromHours(1));
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase("replace")]
        [TestCase("load")]
        [TestCase("reload")]
        public async Task DisposedRegistryCannotPublishNewApplicationCertificateEntriesAsync(string operation)
        {
            using var manager = new CertificateManager(NUnitTelemetryContext.Create());
            using Certificate certificate = CertificateBuilder.Create("CN=Disposed Registry").CreateForRSA();
            using var chain = new CertificateCollection();
            await manager.DisposeAsync().ConfigureAwait(false);
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            {
                if (operation == "replace")
                {
                    await manager.UpdateApplicationCertificateAsync(
                        ObjectTypeIds.RsaSha256ApplicationCertificateType, certificate, chain).ConfigureAwait(false);
                }
                else if (operation == "load")
                {
                    await manager.LoadApplicationCertificatesAsync(new SecurityConfiguration()).ConfigureAwait(false);
                }
                else
                {
                    await manager.ReloadApplicationCertificatesAsync(new SecurityConfiguration()).ConfigureAwait(false);
                }
            });
            using CertificateEntryCollection snapshot = manager.SnapshotApplicationCertificates();
            Assert.That(snapshot, Is.Empty);
            using RSA callerKey = certificate.GetRSAPublicKey()!;
            Assert.That(callerKey.KeySize, Is.EqualTo(2048));
        }

        private sealed class ExpiryObserver(Action<CertificateChangeEvent> onNext) : IObserver<CertificateChangeEvent>
        {
            public void OnNext(CertificateChangeEvent value) => onNext(value);

            public void OnError(Exception error) => Assert.Fail(error.Message);

            public void OnCompleted()
            {
            }
        }
    }
}
