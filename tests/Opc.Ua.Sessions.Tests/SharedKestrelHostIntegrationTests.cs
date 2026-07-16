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
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// End-to-end integration tests for
    /// <see cref="SharedKestrelHostRegistry"/> - verifies that a real
    /// <see cref="HttpsTransportListener"/> registers itself with the
    /// process-wide shared-host registry when it is opened, and releases
    /// its lease (causing the registry to tear down the Kestrel host)
    /// when it is closed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The TLS-cert-thumbprint mismatch, ref-count, and path-routing
    /// behaviours are covered by the registry's unit tests in
    /// <c>tests/Opc.Ua.Core.Tests/Stack/Transport/SharedKestrelHostTests.cs</c>;
    /// this fixture exists to prove the wiring path between
    /// <see cref="HttpsTransportListener.Start"/> /
    /// <see cref="HttpsTransportListener.Dispose"/> and the registry is
    /// connected and survives a full reference-server start/stop cycle.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("SharedKestrelHost")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class SharedKestrelHostIntegrationTests
    {
        private string m_pkiRoot = null!;

        [SetUp]
        public void SetUp()
        {
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (m_pkiRoot != null && Directory.Exists(m_pkiRoot))
                {
                    Directory.Delete(m_pkiRoot, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }

        [Test]
        public async Task WssListenerRegistersWithSharedHostAndReleasesItOnTearDownAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AutoAccept = true,
                SecurityNone = false,
                UriScheme = Utils.UriSchemeOpcWss,
                MaxChannelCount = 4,
                TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security
            };

            try
            {
                _ = await fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
                int port = fixture.Port;
                Assert.That(port, Is.GreaterThan(0), "ServerFixture must bind a non-zero port for shared-host registration.");

                // After Start: at least one listener registered with the
                // shared-host registry. The exact key depends on how
                // HttpsServiceHost rewrites localhost to the computer
                // name; we assert observable side-effects via Count.
                int count = SharedKestrelHostRegistry.Instance.Count;
                Assert.That(count, Is.GreaterThanOrEqualTo(1),
                    "HttpsTransportListener.Start() must register at least one shared host with the registry " +
                    $"(observed total host count = {count}).");
            }
            finally
            {
                int countBeforeStop = SharedKestrelHostRegistry.Instance.Count;
                await fixture.StopAsync().ConfigureAwait(false);

                // After Stop: the lease must be released so the registry no
                // longer references the listener / host. Tear-down is the
                // moment we care about: if the lease leaks here the
                // singleton registry would retain a stale listener forever.
                Assert.That(
                    SharedKestrelHostRegistry.Instance.Count,
                    Is.LessThan(countBeforeStop).Or.Zero,
                    "HttpsTransportListener.Dispose() must release its shared-host lease.");
            }

            await Task.CompletedTask.ConfigureAwait(false);
            GC.KeepAlive(telemetry); // suppress unused-warning if logging is suppressed
            _ = CultureInfo.InvariantCulture;
        }
    }
}
