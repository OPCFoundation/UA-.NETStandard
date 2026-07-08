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

using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Unit tests for the V2 subscription-manager capability guards in
    /// <see cref="ServerSession"/> when the bound <see cref="ISession"/> does
    /// not expose a manager (e.g. a classic-engine session). Uses the internal
    /// session-injecting constructor so the guards are exercised without a
    /// real server.
    /// </summary>
    [TestFixture]
    public sealed class ServerSessionCapabilityGuardTests
    {
        private static ServerSession CreateSessionWithoutManager()
        {
            var mockSession = new Mock<ISession>();
            ISubscriptionManager? manager = null;
            mockSession
                .Setup(s => s.TryGetSubscriptionManager(out manager))
                .Returns(false);
            mockSession.SetupGet(s => s.Connected).Returns(true);

            var options = new ServerConnectionOptions
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            };
            return new ServerSession(
                options, AdapterTestHelpers.Telemetry(), mockSession.Object);
        }

        [Test]
        public void CreateDataChangeSubscriptionThrowsWhenNoV2Manager()
        {
            ServerSession session = CreateSessionWithoutManager();

            Assert.That(
                async () => await session.CreateDataChangeSubscriptionAsync(1000).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo((StatusCode)StatusCodes.BadNotSupported));
        }

        [Test]
        public void StartModelChangeMonitoringNoOpsWhenNoV2Manager()
        {
            ServerSession session = CreateSessionWithoutManager();

            // Model-change monitoring is optional/best-effort: a session without a
            // V2 manager must skip it silently rather than fault.
            Assert.That(
                async () => await session.StartModelChangeMonitoringAsync().ConfigureAwait(false),
                Throws.Nothing);
        }
    }
}
