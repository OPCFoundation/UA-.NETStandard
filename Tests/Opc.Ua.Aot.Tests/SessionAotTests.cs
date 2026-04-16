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
using Opc.Ua.Client;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests for session management operations.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class SessionAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task ReconnectSessionAsync()
        {
            ISession session = await fixture.CreateSessionAsync("Reconnect")
                .ConfigureAwait(false);
            await Assert.That(session.Connected).IsTrue();

            // Force transport disconnect
            ((Session)session).TransportChannel.Dispose();
            await Task.Delay(500).ConfigureAwait(false);

            var reconnected = new TaskCompletionSource<bool>();
            using var handler = new SessionReconnectHandler(fixture.Telemetry);

            handler.BeginReconnect(session, 1000, (_, _) =>
            {
                if (handler.Session?.Connected == true)
                {
                    reconnected.TrySetResult(true);
                }
            });

            using var cts = new CancellationTokenSource(30000);
            cts.Token.Register(() => reconnected.TrySetResult(false));
            bool result = await reconnected.Task.ConfigureAwait(false);

            handler.CancelReconnect();
            await Assert.That(result).IsTrue();

            ISession reconnectedSession = handler.Session ?? session;
            reconnectedSession.DeleteSubscriptionsOnClose = true;
            await reconnectedSession.CloseAsync(CancellationToken.None)
                .ConfigureAwait(false);
            reconnectedSession.Dispose();
        }

        [Test]
        public async Task MultipleSessionsOnServerAsync()
        {
            var sessions = new List<ISession>();

            for (int i = 0; i < 3; i++)
            {
                ISession session = await fixture
                    .CreateSessionAsync($"Multi{i}")
                    .ConfigureAwait(false);
                sessions.Add(session);
            }

            foreach (ISession session in sessions)
            {
                await Assert.That(session.Connected).IsTrue();
            }

            await Assert.That(sessions.Count).IsEqualTo(3);

            foreach (ISession session in sessions)
            {
                session.DeleteSubscriptionsOnClose = true;
                await session.CloseAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        public async Task ChangePreferredLocalesAsync()
        {
            ISession session = await fixture
                .CreateSessionAsync("ChangeLocales")
                .ConfigureAwait(false);
            await Assert.That(session.Connected).IsTrue();

            ArrayOf<string> locales = ["en-US", "de-DE"];
            await session.ChangePreferredLocalesAsync(
                locales, CancellationToken.None).ConfigureAwait(false);

            await Assert.That(session.PreferredLocales.Count)
                .IsEqualTo(2);

            await session.CloseAsync(CancellationToken.None)
                .ConfigureAwait(false);
            session.Dispose();
        }
    }
}
