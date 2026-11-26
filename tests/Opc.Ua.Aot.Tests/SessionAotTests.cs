// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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

            // Force transport disconnect without disposing the channel wrapper
            // that SessionReconnectHandler reconnects.
            await ((Session)session).TransportChannel
                .CloseAsync(CancellationToken.None).ConfigureAwait(false);
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
