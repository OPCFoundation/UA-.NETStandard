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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Part 5 §6.3.1: every change of the ServerArray or the NamespaceArray raises
    /// Server.UrisVersion, a VersionTime (Part 4 §7.43) that is not 0 and grows.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [NonParallelizable]
    public class ServerUrisVersionTests
    {
        private ServerFixture<StandardServer> m_fixture = null!;
        private StandardServer m_server = null!;
        private uint m_versionTimeBeforeStart;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_versionTimeBeforeStart = ServerUrisVersion.ToVersionTime(DateTime.UtcNow);
            m_fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        [Test]
        public void ToVersionTimeCountsSecondsSince2000()
        {
            Assert.That(
                ServerUrisVersion.ToVersionTime(new DateTime(2000, 1, 1, 0, 1, 40, DateTimeKind.Utc)),
                Is.EqualTo(100u));
            Assert.That(
                ServerUrisVersion.ToVersionTime(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                Is.EqualTo(1u),
                "0 means no version information.");
            Assert.That(
                ServerUrisVersion.ToVersionTime(new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                Is.EqualTo(1u));
        }

        [Test]
        public void UrisVersionStartsAtServerStartTime()
        {
            uint value = ReadUrisVersion();

            Assert.That(value, Is.GreaterThanOrEqualTo(m_versionTimeBeforeStart));
            Assert.That(value, Is.LessThanOrEqualTo(ServerUrisVersion.ToVersionTime(DateTime.UtcNow) + 60));
        }

        [Test]
        public void UrisVersionIsRaisedAndReportedWhenNamespaceIsRegistered()
        {
            PropertyState<uint> urisVersion = GetUrisVersion();
            uint before = ReadUrisVersion();
            int changes = 0;
            void OnStateChanged(ISystemContext context, NodeState node, NodeStateChangeMasks changeMasks)
            {
                if ((changeMasks & NodeStateChangeMasks.Value) != 0)
                {
                    changes++;
                }
            }

            string namespaceUri = "urn:opcfoundation.org:Tests:UrisVersion:" + Guid.NewGuid().ToString("N");
            var nodeManager = new Mock<INodeManager>();
            nodeManager.Setup(x => x.NamespaceUris).Returns([]);

            urisVersion.StateChanged += OnStateChanged;
            try
            {
                m_server.CurrentInstance.NodeManager.RegisterNamespaceManager(namespaceUri, nodeManager.Object);
            }
            finally
            {
                urisVersion.StateChanged -= OnStateChanged;
                m_server.CurrentInstance.NodeManager.UnregisterNamespaceManager(namespaceUri, nodeManager.Object);
            }

            Assert.That(changes, Is.EqualTo(1), "Subscribers are notified of the new UrisVersion.");
            Assert.That(urisVersion.Value, Is.GreaterThan(before));
            Assert.That(ReadUrisVersion(), Is.EqualTo(urisVersion.Value));
        }

        [Test]
        public void UrisVersionIsRaisedWhenServerUriIsAdded()
        {
            uint before = ReadUrisVersion();

            m_server.CurrentInstance.ServerUris.Append(
                "urn:opcfoundation.org:Tests:UrisVersion:Server:" + Guid.NewGuid().ToString("N"));

            uint after = ReadUrisVersion();
            Assert.That(after, Is.GreaterThan(before));
            Assert.That(ReadUrisVersion(), Is.EqualTo(after), "UrisVersion does not change without a table change.");
        }

        /// <summary>
        /// Monitored items of UrisVersion are sampled like NamespaceArray and ServerArray,
        /// so a table change no caller reported reaches them without a client Read.
        /// </summary>
        [Test]
        public void UrisVersionIsSampled()
        {
            Assert.That(GetUrisVersion().MinimumSamplingInterval, Is.GreaterThan(0));
        }

        [Test]
        public void UrisVersionIsRaisedWhileServerIsSuspended()
        {
            IServerInternal server = m_server.CurrentInstance;
            uint before = ReadUrisVersion();
            server.UpdateServerStatus(status => status.Value.State = ServerState.Suspended);
            try
            {
                Assert.That(server.IsRunning, Is.False);
                server.NamespaceUris.Append(
                    "urn:opcfoundation.org:Tests:UrisVersion:Suspended:" + Guid.NewGuid().ToString("N"));
                ServerUrisVersion.Update(server);

                Assert.That(GetUrisVersion().Value, Is.GreaterThan(before));
            }
            finally
            {
                server.UpdateServerStatus(status => status.Value.State = ServerState.Running);
            }

            Assert.That(ReadUrisVersion(), Is.GreaterThan(before));
        }

        private PropertyState<uint> GetUrisVersion()
        {
            ServerObjectState serverObject = m_server.CurrentInstance.DiagnosticsNodeManager
                .FindPredefinedNode<ServerObjectState>(ObjectIds.Server);
            Assert.That(serverObject?.UrisVersion, Is.Not.Null);
            return serverObject!.UrisVersion!;
        }

        private uint ReadUrisVersion()
        {
            var value = new DataValue();
            ServiceResult result = GetUrisVersion().ReadAttribute(
                m_server.CurrentInstance.DefaultSystemContext,
                Attributes.Value,
                default,
                default,
                ref value);
            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
            Assert.That(value.WrappedValue.TryGetValue(out uint version), Is.True);
            return version;
        }
    }
}
