// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    /// <summary>
    /// Verifies against a running server that a session connected through
    /// <see cref="ManagedSessionBuilder.UseEndpoint(string, MessageSecurityMode, string)"/>
    /// decodes with the application's transport quotas (issue #4905).
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("Client")]
    [Category("Integration")]
    public sealed class ManagedSessionBuilderTransportQuotaTests
    {
        /// <summary>
        /// The session message context reflects the configured limits rather
        /// than the built-in endpoint defaults.
        /// </summary>
        [Test]
        public async Task UseEndpointUrlSessionUsesApplicationTransportQuotasAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string root = Path.Combine(Path.GetTempPath(), "mtq-" + Guid.NewGuid().ToString("N")[..8]);
            var fixture = new ServerFixture<ReferenceServer>(context => new ReferenceServer(context))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            try
            {
                await fixture.StartAsync(root).ConfigureAwait(false);
                await using var client = new ClientFixture(telemetry);
                await client.LoadClientConfigurationAsync(root, "TransportQuotaClient").ConfigureAwait(false);
                ApplicationConfiguration configuration = client.Config;
                configuration.TransportQuotas = new TransportQuotas
                {
                    MaxStringLength = 1024 * 1024,
                    MaxByteStringLength = 2 * 1024 * 1024,
                    MaxArrayLength = 300_000,
                    MaxMessageSize = 8 * 1024 * 1024
                };
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

                await using Client.ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
                    .UseEndpoint(
                        $"{Utils.UriSchemeOpcTcp}://localhost:{fixture.Port}",
                        MessageSecurityMode.None,
                        SecurityPolicies.None)
                    .ConnectAsync(timeout.Token)
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(session.MessageContext.MaxStringLength, Is.EqualTo(1024 * 1024));
                    Assert.That(session.MessageContext.MaxByteStringLength, Is.EqualTo(2 * 1024 * 1024));
                    Assert.That(session.MessageContext.MaxArrayLength, Is.EqualTo(300_000));
                    Assert.That(session.MessageContext.MaxMessageSize, Is.EqualTo(8 * 1024 * 1024));
                });
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }
}
