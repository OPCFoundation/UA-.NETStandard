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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Core.TestFramework;
using Opc.Ua.Redundancy;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    [TestFixture]
    [Category("CertificateStore")]
    public sealed class RejectedCertificateLimitRegressionTests
    {
        [Test]
        public async Task ManagerPreservesUnlimitedAndDisabledLimitsWhenTrimmingAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var temporary = TemporaryCertificateManager.Create(telemetry, true);
            CertificateManager manager = temporary.Update();
            manager.MaxRejectedCertificates = 0;
            for (int i = 0; i < 3; i++)
            {
                using Certificate certificate = CertificateBuilder.Create("CN=ManagerRejected" + i).CreateForRSA();
                using var chain = new CertificateCollection { certificate };
                CertificateValidationResult result = await manager.ValidateAsync(chain).ConfigureAwait(false);
                Assert.That(result.IsValid, Is.False);
            }
            await manager.FlushRejectedAsync().ConfigureAwait(false);
            using (CertificateCollection retained = await temporary.RejectedStore.EnumerateAsync().ConfigureAwait(false))
            {
                Assert.That(retained, Has.Count.EqualTo(3));
            }
            manager.MaxRejectedCertificates = -1;
            await manager.FlushRejectedAsync().ConfigureAwait(false);
            using (CertificateCollection retained = await temporary.RejectedStore.EnumerateAsync().ConfigureAwait(false))
            {
                Assert.That(retained, Is.Empty);
            }
            using Certificate disabled = CertificateBuilder.Create("CN=ManagerRejectedDisabled").CreateForRSA();
            using var disabledChain = new CertificateCollection { disabled };
            Assert.That((await manager.ValidateAsync(disabledChain).ConfigureAwait(false)).IsValid, Is.False);
            await manager.FlushRejectedAsync().ConfigureAwait(false);
            using CertificateCollection final = await temporary.RejectedStore.EnumerateAsync().ConfigureAwait(false);
            Assert.That(final, Is.Empty);
        }

        [Test]
        public async Task RejectedHistoryUsesZeroAsUnlimitedAndNegativeAsDisabledAsync(
            [Values(false, true)] bool shared,
            [Values(-1, 0, 1, 2)] int limit)
        {
            string path = Path.Combine(Path.GetTempPath(), "RejectedLimit-" + Guid.NewGuid().ToString("N"));
            var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
            using var backend = new InMemorySharedKeyValueStore();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using ICertificateStore store = shared
                ? new SharedKeyValueCertificateStore(backend, null, telemetry)
                : new DirectoryCertificateStore(true, telemetry, time);
            store.Open(shared ? "kv:rejected-limit" : path, noPrivateKeys: true);
            using var certificates = new CertificateCollection();
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    using Certificate certificate = CertificateBuilder.Create("CN=RejectedLimit" + i).CreateForRSA();
                    certificates.Add(certificate);
                    using var chain = new CertificateCollection { certificate };
                    await store.AddRejectedAsync(chain, limit).ConfigureAwait(false);
                    time.Advance(TimeSpan.FromSeconds(1));
                }
                using CertificateCollection retained = await store.EnumerateAsync().ConfigureAwait(false);
                int expected = limit < 0 ? 0 : limit == 0 ? 3 : limit;
                Assert.That(retained, Has.Count.EqualTo(expected));
                if (limit == 0 || (!shared && limit > 0))
                {
                    Assert.That(retained.Select(certificate => certificate.Thumbprint),
                        Is.EquivalentTo(certificates.Skip(3 - expected).Select(certificate => certificate.Thumbprint)));
                }
                if (!shared)
                {
                    using var empty = new CertificateCollection();
                    await store.AddRejectedAsync(empty, -1).ConfigureAwait(false);
                    using CertificateCollection cleared = await store.EnumerateAsync().ConfigureAwait(false);
                    Assert.That(cleared, Is.Empty);
                }
            }
            finally
            {
                store.Close();
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }
    }
}
