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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("ServiceLevel")]
    public sealed class ServerServiceLevelOwnershipTests
    {
        [SetUp]
        public async Task StartServerAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
            {
                SecurityNone = true
            };
            await m_fixture.LoadConfigurationAsync().ConfigureAwait(false);
            m_fixture.Config.ServerConfiguration.MaxSessionCount = 1;
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
            EndpointDescription endpoint = m_server.GetEndpoints().Find(
                description => description.SecurityPolicyUri == SecurityPolicies.None);
            m_channel = new SecureChannelContext("service-level", endpoint, RequestEncoding.Binary);
        }

        [TearDown]
        public async Task StopServerAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        [TestCase((byte)210)]
        [TestCase((byte)255)]
        public async Task ProviderOwnedHealthyLevelSurvivesSessionChurnAsync(byte initialLevel)
        {
            var provider = new MutableProvider(initialLevel);
            var startup = new ServiceLevelStartupTask(provider);
            await startup.OnServerStartedAsync(m_server.CurrentInstance).ConfigureAwait(false);
            Assert.That(m_server.CurrentInstance.ServerObject.ServiceLevel.Value, Is.EqualTo(initialLevel));

            CreateSessionResponse created = await CreateSessionAsync().ConfigureAwait(false);
            Assert.That(m_server.CurrentInstance.ServerObject.ServiceLevel.Value, Is.EqualTo(initialLevel));
            provider.SetLevel(235);
            Assert.That(m_server.CurrentInstance.ServerObject.ServiceLevel.Value, Is.EqualTo((byte)235));

            await CloseSessionAsync(created).ConfigureAwait(false);
            Assert.That(m_server.CurrentInstance.ServerObject.ServiceLevel.Value, Is.EqualTo((byte)235));
        }

        [Test]
        public async Task ASecondProviderCannotTakeServiceLevelOwnershipAsync()
        {
            var first = new MutableProvider(210);
            var second = new MutableProvider(240);
            await new ServiceLevelStartupTask(first).OnServerStartedAsync(m_server.CurrentInstance)
                .ConfigureAwait(false);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await new ServiceLevelStartupTask(second).OnServerStartedAsync(m_server.CurrentInstance)
                    .ConfigureAwait(false));
            second.SetLevel(255);
            Assert.That(m_server.CurrentInstance.ServerObject.ServiceLevel.Value, Is.EqualTo((byte)210));
            first.SetLevel(215);
            Assert.That(m_server.CurrentInstance.ServerObject.ServiceLevel.Value, Is.EqualTo((byte)215));
        }

        [Test]
        public async Task UnownedServiceLevelStillTracksSessionHeadroomAsync()
        {
            Assert.That(m_server.CurrentInstance.ServerObject.ServiceLevel.Value, Is.EqualTo((byte)255));
            CreateSessionResponse created = await CreateSessionAsync().ConfigureAwait(false);
            Assert.That(m_server.CurrentInstance.ServerObject.ServiceLevel.Value, Is.EqualTo((byte)200));
            await CloseSessionAsync(created).ConfigureAwait(false);
            Assert.That(m_server.CurrentInstance.ServerObject.ServiceLevel.Value, Is.EqualTo((byte)255));
        }

        private ValueTask<CreateSessionResponse> CreateSessionAsync()
        {
            return m_server.CreateSessionAsync(
                m_channel, new RequestHeader(), null, null, null, "service-level",
                default, default, 60000, 0, RequestLifetime.None);
        }

        private ValueTask<CloseSessionResponse> CloseSessionAsync(CreateSessionResponse session)
        {
            return m_server.CloseSessionAsync(
                m_channel, new RequestHeader { AuthenticationToken = session.AuthenticationToken },
                true, RequestLifetime.None);
        }

        private ServerFixture<StandardServer> m_fixture;
        private StandardServer m_server;
        private SecureChannelContext m_channel;

        private sealed class MutableProvider(byte level) : IServiceLevelProvider
        {
            public event Action<byte> ServiceLevelChanged;

            public byte GetServiceLevel() => m_level;

            public void SetLevel(byte value)
            {
                m_level = value;
                ServiceLevelChanged?.Invoke(value);
            }

            private byte m_level = level;
        }
    }
}
