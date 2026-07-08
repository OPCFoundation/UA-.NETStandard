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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
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
