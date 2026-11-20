// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.CranesHoists.Tests
{
    /// <summary>
    /// Owns a real opc.tcp test session and test-local PKI configuration.
    /// </summary>
    internal sealed class CraneClientConnection : IAsyncDisposable
    {
        /// <summary>
        /// Captures successfully connected resources.
        /// </summary>
        private CraneClientConnection(ClientFixture fixture, ISession session)
        {
            m_fixture = fixture;
            Session = session;
        }

        /// <summary>
        /// Gets the real client session.
        /// </summary>
        internal ISession Session { get; }

        /// <summary>
        /// Connects using the existing shared client test framework and V2 subscriptions.
        /// </summary>
        internal static async Task<CraneClientConnection> ConnectAsync(CraneServerFixture server, bool managed = false)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var fixture = new ClientFixture(telemetry);
            try
            {
                fixture.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
                if (managed)
                {
                    fixture.SessionFactory = new ManagedSessionFactory(telemetry)
                        .WithSubscriptionEngine(DefaultSubscriptionEngineFactory.Instance);
                }
                await fixture.LoadClientConfigurationAsync(
                    CraneTestPaths.NewPkiPath(),
                    "CraneInteropClient").ConfigureAwait(false);
                ISession session = await fixture.ConnectAsync(
                    new Uri(server.Configuration.ServerConfiguration!.BaseAddresses[0]),
                    SecurityPolicies.None).ConfigureAwait(false);
                return new CraneClientConnection(fixture, session);
            }
            catch
            {
                await fixture.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Session.Connected)
                {
                    await Session.CloseAsync(1000, true).ConfigureAwait(false);
                }
                if (Session is IAsyncDisposable asynchronous)
                {
                    await asynchronous.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    Session.Dispose();
                }
            }
            finally
            {
                await m_fixture.DisposeAsync().ConfigureAwait(false);
            }
        }

        private readonly ClientFixture m_fixture;
    }
}
