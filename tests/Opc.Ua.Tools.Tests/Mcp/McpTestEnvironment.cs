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

// Opc.Ua.Mcp targets net10.0 only, so the MCP integration fixtures only
// build and run on net10.0.
#if NET10_0
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Pcap.DependencyInjection;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Starts a single in-process <see cref="ReferenceServer"/> and a
    /// connected <see cref="OpcUaSessionManager"/> for the whole
    /// <c>Opc.Ua.Tools.Tests.Mcp</c> namespace, so the many MCP tool test
    /// fixtures can exercise real read/write/browse/call/subscribe behavior
    /// against a live server without each fixture paying the cost (and
    /// added flakiness) of starting its own server and application
    /// certificate.
    /// </summary>
    [SetUpFixture]
    public sealed class McpTestEnvironment
    {
        /// <summary>
        /// The name under which the shared session is registered with
        /// <see cref="SessionManager"/>.
        /// </summary>
        public const string SessionName = "mcp-tests";

        public static ServerFixture<ReferenceServer> ServerFixture { get; private set; } = null!;

        public static ReferenceServer Server { get; private set; } = null!;

        public static string ServerUrl { get; private set; } = null!;

        public static ServiceProvider Services { get; private set; } = null!;

        public static OpcUaSessionManager SessionManager { get; private set; } = null!;

        public static string PcapBaseFolder { get; private set; } = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            string pkiRoot = Path.Combine(Path.GetTempPath(), "mcp-tests-pki-" + Path.GetRandomFileName());

            ServerFixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AutoAccept = true,
                SecurityNone = true,
                OperationLimits = false,
                AllNodeManagers = true
            };

            Server = await ServerFixture.StartAsync(pkiRoot).ConfigureAwait(false);

            ServerUrl = string.Format(
                CultureInfo.InvariantCulture,
                "{0}://localhost:{1}",
                Utils.UriSchemeOpcTcp,
                ServerFixture.Port);

            PcapBaseFolder = Path.Combine(Path.GetTempPath(), "mcp-tests-pcap-" + Path.GetRandomFileName());

            var services = new ServiceCollection();
            McpHostBuilder.ConfigureServices(
                services,
                new PcapOptions { BaseFolder = PcapBaseFolder });
            Services = services.BuildServiceProvider();

            SessionManager = Services.GetRequiredService<OpcUaSessionManager>();

            await SessionManager.ConnectAsync(
                SessionName,
                ServerUrl,
                securityMode: null,
                securityPolicy: null,
                authType: "Anonymous",
                username: null,
                password: null,
                autoAcceptCerts: true,
                CancellationToken.None).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (SessionManager != null)
            {
                await SessionManager.DisconnectAsync(SessionName).ConfigureAwait(false);
                SessionManager.Dispose();
            }

            Services?.Dispose();

            if (ServerFixture != null)
            {
                await ServerFixture.StopAsync().ConfigureAwait(false);
            }
        }
    }
}
#endif
