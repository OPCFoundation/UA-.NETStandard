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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client.Tests.AuditRegressions
{
    /// <summary>
    /// Regressions for the session-core defects (L1-x) reported by the
    /// Opc.Ua.Client audit. Each test names the defect it pins down.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("AuditRegression")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SessionCoreAuditRegressionTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>
        /// L1-5: Restore loaded the endpoint's server certificate with
        /// Certificate.FromRawData, which throws on macOS for a chain blob
        /// (leaf + issuers). The leaf must be parsed out of the chain.
        /// </summary>
        [Test]
        public void RestoreParsesServerCertificateChainBlob()
        {
            using Certificate issuer = CertificateBuilder.Create("CN=RestoreIssuer")
                .SetCAConstraint()
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder.Create("CN=RestoreLeaf")
                .SetIssuer(issuer)
                .CreateForRSA();
            var endpoint = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                EndpointUrl = "opc.tcp://localhost:4840",
                ServerCertificate = ByteString.From([.. leaf.RawData, .. issuer.RawData]),
                UserIdentityTokens = [new UserTokenPolicy()]
            };
            using SessionMock session = SessionMock.Create(endpoint);

            session.Restore(new SessionConfiguration
            {
                SessionName = "restored",
                SessionId = new NodeId("session", 3),
                AuthenticationToken = new NodeId("token", 3)
            });

            var restored = (Certificate)typeof(Session)
                .GetField("m_serverCertificate", PrivateInstance)!
                .GetValue(session)!;
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.Thumbprint, Is.EqualTo(leaf.Thumbprint));
        }

        /// <summary>
        /// L1-3: disposing the session from a handler running on its
        /// background work (Session.Notification) waited for the drain of
        /// that same handler, i.e. the full 30 s drain timeout.
        /// </summary>
        [Test]
        public async Task DisposeFromBackgroundHandlerDoesNotWaitForItself()
        {
            SessionMock session = SessionMock.Create();
            var disposed = new TaskCompletionSource<TimeSpan>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.That(session.RunBackgroundWork("test", () =>
            {
                var watch = Stopwatch.StartNew();
                session.Dispose();
                disposed.SetResult(watch.Elapsed);
            }), Is.True);

            TimeSpan elapsed = await disposed.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            Assert.That(elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        }
    }
}
