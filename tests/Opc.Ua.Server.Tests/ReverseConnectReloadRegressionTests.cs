/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies reverse-connect configuration replacement without losing dynamically registered connections.
    /// </summary>
    [TestFixture]
    [Category("ReverseConnect")]
    public sealed class ReverseConnectReloadRegressionTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public async Task MissingOrEmptyReverseConfigurationRemovesOnlyConfiguredConnectionsAsync(bool omitSection)
        {
            var fixture = new ServerFixture<ReloadableReverseConnectServer>(
                telemetry => new ReloadableReverseConnectServer(telemetry));
            try
            {
                await fixture.LoadConfigurationAsync().ConfigureAwait(false);
                fixture.Config.ServerConfiguration!.ReverseConnect =
                    CreateConfiguration("configured").ServerConfiguration!.ReverseConnect;
                ReloadableReverseConnectServer server = await fixture.StartAsync().ConfigureAwait(false);
                var dynamicUrl = new Uri("opc.tcp://localhost:4840/dynamic");
                server.AddReverseConnection(dynamicUrl, timeout: 1234, enabled: false);
                ReverseConnectProperty retained = server.GetReverseConnections()[dynamicUrl];
                Assert.That(server.GetReverseConnections(), Has.Count.EqualTo(2));

                fixture.Config.ServerConfiguration.ReverseConnect = omitSection
                    ? null
                    : new ReverseConnectServerConfiguration { Clients = [] };
                await server.ReloadAsync(fixture.Config).ConfigureAwait(false);

                Assert.That(server.GetReverseConnections(), Has.Count.EqualTo(1));
                Assert.That(server.GetReverseConnections()[dynamicUrl], Is.SameAs(retained));
                Assert.That(retained.ConfigEntry, Is.False);
                Assert.That(retained.Timeout, Is.EqualTo(1234));
                Assert.That(retained.Enabled, Is.False);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConfiguredDuplicateCannotOverwriteProgrammaticConnectionAsync()
        {
            var fixture = new ServerFixture<ReloadableReverseConnectServer>(
                telemetry => new ReloadableReverseConnectServer(telemetry));
            try
            {
                ReloadableReverseConnectServer server = await fixture.StartAsync().ConfigureAwait(false);
                var dynamicUrl = new Uri("opc.tcp://localhost:4840/dynamic");
                server.AddReverseConnection(dynamicUrl, timeout: 1234, enabled: false);
                ReverseConnectProperty retained = server.GetReverseConnections()[dynamicUrl];
                fixture.Config.ServerConfiguration!.ReverseConnect = new ReverseConnectServerConfiguration
                {
                    Clients =
                    [
                        new ReverseConnectClient
                        {
                            EndpointUrl = dynamicUrl.AbsoluteUri,
                            Timeout = 9000,
                            Enabled = true
                        }
                    ]
                };

                await server.ReloadAsync(fixture.Config).ConfigureAwait(false);
                await server.ReloadAsync(fixture.Config).ConfigureAwait(false);

                Assert.That(server.GetReverseConnections(), Has.Count.EqualTo(1));
                Assert.That(server.GetReverseConnections()[dynamicUrl], Is.SameAs(retained));
                Assert.That(retained.ConfigEntry, Is.False);
                Assert.That(retained.Timeout, Is.EqualTo(1234));
                Assert.That(retained.Enabled, Is.False);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that reload removes obsolete configured connections while retaining dynamic connection settings.
        /// </summary>
        [Test]
        public void ReloadRemovesEveryConfiguredEntryAndPreservesDynamicConnections()
        {
            using var server = new ReverseConnectServer(NUnitTelemetryContext.Create(), new FakeTimeProvider());
            var dynamicUrl = new Uri("opc.tcp://localhost:4840/dynamic");
            server.AddReverseConnection(dynamicUrl, timeout: 1234, enabled: false);
            MethodInfo method = typeof(ReverseConnectServer).GetMethod(
                "UpdateConfiguration", BindingFlags.Instance | BindingFlags.NonPublic)!;
#if NET5_0_OR_GREATER
            Action<ApplicationConfiguration> update = method.CreateDelegate<Action<ApplicationConfiguration>>(server);
#else
            var update = (Action<ApplicationConfiguration>)method.CreateDelegate(
                typeof(Action<ApplicationConfiguration>), server);
#endif
            update(CreateConfiguration("first", "second", "third"));
            Assert.That(server.GetReverseConnections(), Has.Count.EqualTo(4));
            update(CreateConfiguration("replacement"));
            Assert.That(server.GetReverseConnections(), Has.Count.EqualTo(2));
            Assert.That(server.GetReverseConnections().ContainsKey(new Uri("opc.tcp://localhost:4840/replacement")),
                Is.True);
            update(CreateConfiguration());
            Assert.That(server.GetReverseConnections(), Has.Count.EqualTo(1));
            ReverseConnectProperty retained = server.GetReverseConnections()[dynamicUrl];
            Assert.That(retained.ConfigEntry, Is.False);
            Assert.That(retained.Timeout, Is.EqualTo(1234));
            Assert.That(retained.Enabled, Is.False);
        }

        /// <summary>
        /// Creates disabled reverse-connect entries whose endpoint paths identify each configured connection.
        /// </summary>
        private static ApplicationConfiguration CreateConfiguration(params string[] names)
        {
            var clients = new ReverseConnectClient[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                clients[i] = new ReverseConnectClient
                {
                    EndpointUrl = "opc.tcp://localhost:4840/" + names[i],
                    Enabled = false
                };
            }

            return new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    ReverseConnect = new ReverseConnectServerConfiguration
                    {
                        Clients = clients
                    }
                }
            };
        }

        private sealed class ReloadableReverseConnectServer(ITelemetryContext telemetry)
            : ReverseConnectServer(telemetry, new FakeTimeProvider())
        {
            public ValueTask ReloadAsync(ApplicationConfiguration configuration)
            {
                return OnUpdateConfigurationAsync(configuration);
            }
        }
    }
}
