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
