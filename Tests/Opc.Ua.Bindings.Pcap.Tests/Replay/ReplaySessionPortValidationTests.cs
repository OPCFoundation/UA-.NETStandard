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
using Opc.Ua.Bindings.Pcap.Capture;
using Opc.Ua.Bindings.Pcap.Replay;

namespace Opc.Ua.Bindings.Pcap.Tests.Replay
{
    /// <summary>
    /// Tests listen-port validation at the mock-server replay session entry
    /// point before any network listener is started.
    /// </summary>
    [TestFixture]
    public sealed class ReplaySessionPortValidationTests
    {
        /// <summary>
        /// Verifies that privileged and wildcard listen ports are rejected
        /// before the replay session stores the requested port.
        /// </summary>
        [TestCase(0)]
        [TestCase(80)]
        [TestCase(443)]
        [TestCase(1023)]
        public async Task StartReplayRejectsPrivilegedPortBelow1024(int listenPort)
        {
            await AssertConstructorRejectsPortAsync(listenPort).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that listen ports above the TCP port range are rejected
        /// before the replay session stores the requested port.
        /// </summary>
        [TestCase(65536)]
        [TestCase(100000)]
        public async Task StartReplayRejectsPortAbove65535(int listenPort)
        {
            await AssertConstructorRejectsPortAsync(listenPort).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that negative listen ports are rejected before the replay
        /// session stores the requested port.
        /// </summary>
        [TestCase(-1)]
        [TestCase(-1024)]
        public async Task StartReplayRejectsNegativePort(int listenPort)
        {
            await AssertConstructorRejectsPortAsync(listenPort).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that a null listen port is accepted so the OS can assign
        /// an ephemeral port when the replay session is started.
        /// </summary>
        [Test]
        public async Task StartReplayAcceptsNullPort()
        {
            await AssertConstructorAcceptsPortAsync(null).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that allowed listen-port boundary values and an ephemeral
        /// port from the dynamic range are accepted.
        /// </summary>
        [TestCase(1024)]
        [TestCase(65535)]
        [TestCase(49152)]
        public async Task StartReplayAcceptsValidPortRangeBoundaries(int listenPort)
        {
            await AssertConstructorAcceptsPortAsync(listenPort).ConfigureAwait(false);
        }

        private static async Task AssertConstructorRejectsPortAsync(int listenPort)
        {
            var source = new InMemoryCaptureSource();
            await using (source.ConfigureAwait(false))
            {
                var mockServer = new MockServerReplay(source);
                await using (mockServer.ConfigureAwait(false))
                {
                    Assert.That(
                        () => new ReplaySession(
                            "invalid-port",
                            mockServer,
                            listenScheme: "opc.tcp",
                            listenPort: listenPort),
                        Throws.TypeOf<ArgumentOutOfRangeException>()
                            .With.Property("ParamName").EqualTo("listenPort"));
                }
            }
        }

        private static async Task AssertConstructorAcceptsPortAsync(int? listenPort)
        {
            var source = new InMemoryCaptureSource();
            await using (source.ConfigureAwait(false))
            {
                var mockServer = new MockServerReplay(source);
                await using (mockServer.ConfigureAwait(false))
                {
                    var session = new ReplaySession(
                        "valid-port",
                        mockServer,
                        listenScheme: "opc.tcp",
                        listenPort: listenPort);

                    await using (session.ConfigureAwait(false))
                    {
                        Assert.That(session.IsRunning, Is.False);
                    }
                }
            }
        }
    }
}
