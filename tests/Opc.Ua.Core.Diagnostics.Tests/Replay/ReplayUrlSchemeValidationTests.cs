/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.DependencyInjection;
using Opc.Ua.Pcap.Replay;

namespace Opc.Ua.Pcap.Tests.Replay
{
    /// <summary>
    /// Tests mock-client replay target endpoint URL scheme validation.
    /// </summary>
    [TestFixture]
    public sealed class ReplayUrlSchemeValidationTests
    {
        [Test]
        public void MockClientReplayRejectsHttpScheme()
        {
            Assert.That(
                () => CreateReplay("http://localhost:80"),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("scheme"));
        }

        [Test]
        public void MockClientReplayRejectsFileScheme()
        {
            Assert.That(
                () => CreateReplay("file:///etc/passwd"),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("scheme"));
        }

        [Test]
        public void MockClientReplayRejectsLdapScheme()
        {
            Assert.That(
                () => CreateReplay("ldap://localhost:389"),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("scheme"));
        }

        [Test]
        public async Task MockClientReplayAcceptsOpcTcpScheme()
        {
            await AssertEndpointAcceptedAsync("opc.tcp://localhost:4840").ConfigureAwait(false);
        }

        [Test]
        public async Task MockClientReplayAcceptsOpcTcpsScheme()
        {
            await AssertEndpointAcceptedAsync("opc.tcps://localhost:4843").ConfigureAwait(false);
        }

        [Test]
        public async Task MockClientReplayAcceptsOpcHttpsScheme()
        {
            await AssertEndpointAcceptedAsync("opc.https://host.example.com:443").ConfigureAwait(false);
        }

        private static async Task AssertEndpointAcceptedAsync(string targetEndpointUrl)
        {
            MockClientReplay replay = CreateReplay(targetEndpointUrl);
            await replay.DisposeAsync().ConfigureAwait(false);
        }

        private static MockClientReplay CreateReplay(string targetEndpointUrl)
        {
            var options = new PcapOptions
            {
                AllowMockClientReplay = true,
                AllowedReplayEndpoints = ["localhost", "host.example.com"]
            };

            return new MockClientReplay(new InMemoryCaptureSource(), targetEndpointUrl, options);
        }
    }
}
