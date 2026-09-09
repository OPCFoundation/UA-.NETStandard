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
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test Standard Server startup.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ServerStartupTests
    {
        [DatapointSource]
        public string[] UriSchemes =
        [
            Utils.UriSchemeOpcTcp,
            Utils.UriSchemeHttps,
            Utils.UriSchemeOpcHttps
        ];

        /// <summary>
        /// Start a server fixture with different uri schemes.
        /// </summary>
        [Theory]
        public async Task StartServerAsync(string uriScheme)
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            Assert.That(fixture, Is.Not.Null);
            fixture.UriScheme = uriScheme;
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
            Assert.That(server, Is.Not.Null);
            await Task.Delay(1000).ConfigureAwait(false);
            await fixture.StopAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// The port a fixture is handed is only known to be free at the moment
        /// it is picked, so a parallel fixture or an unrelated process on the
        /// agent can take it before the server binds. Every transport's way of
        /// reporting that has to be recognised as retryable: the https
        /// listeners bind through Kestrel, which surfaces the collision as an
        /// IOException wrapping an AddressInUseException rather than as a
        /// ServiceResultException, and used to escape the retry and fail the
        /// caller's whole OneTimeSetUp.
        /// </summary>
        [Test]
        public void IsPortUnavailableRecognisesEveryTransportsPortCollision()
        {
            var uaTcp = new ServiceResultException(
                StatusCodes.BadNoCommunication, "Failed to open a listening socket.");
            var inUse = new SocketException((int)SocketError.AddressAlreadyInUse);
            var kestrel = new IOException(
                "Failed to bind to address https://[::]:49254: address already in use.",
                new AddressInUseException("Address already in use.", inUse));

            Assert.Multiple(() =>
            {
                Assert.That(ServerFixtureUtils.IsPortUnavailable(uaTcp), Is.True);
                Assert.That(ServerFixtureUtils.IsPortUnavailable(inUse), Is.True);
                Assert.That(ServerFixtureUtils.IsPortUnavailable(kestrel), Is.True);
                Assert.That(
                    ServerFixtureUtils.IsPortUnavailable(new AggregateException(kestrel)),
                    Is.True);
            });
        }

        /// <summary>
        /// A failure that has nothing to do with the port must not be retried
        /// on a different one - it would only hide the real error behind a
        /// second, identical failure.
        /// </summary>
        [Test]
        public void IsPortUnavailableIgnoresUnrelatedFailures()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    ServerFixtureUtils.IsPortUnavailable(
                        new InvalidOperationException("Application instance certificate invalid!")),
                    Is.False);
                Assert.That(
                    ServerFixtureUtils.IsPortUnavailable(
                        new ServiceResultException(StatusCodes.BadCertificateInvalid)),
                    Is.False);
                Assert.That(
                    ServerFixtureUtils.IsPortUnavailable(
                        new SocketException((int)SocketError.ConnectionRefused)),
                    Is.False);
            });
        }
    }
}
