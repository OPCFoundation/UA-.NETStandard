/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Recycling.OperatorConsole;
using Opc.Ua.Recycling.Samples.Tests;

namespace Opc.Ua.Recycling.Operator.Tests.Backend
{
    /// <summary>
    /// Exercises browser authentication and command handling through the real encrypted UA coordinator interface.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class ConsoleUaIntegrationTests
    {
        /// <summary>
        /// HTTP security, typed subscriptions and an authorized coordinator mutation
        /// compose against an actual server, without browser-generated plant data.
        /// </summary>
        [TestCase(null)]
        [TestCase(RecyclingProcess.Collection)]
        [TestCase(RecyclingProcess.Sorting)]
        [TestCase(RecyclingProcess.Shredding)]
        [TestCase(RecyclingProcess.Washing)]
        [TestCase(RecyclingProcess.DensitySeparation)]
        [TestCase(RecyclingProcess.Drying)]
        [TestCase(RecyclingProcess.Extrusion)]
        [TestCase(RecyclingProcess.MeltFiltration)]
        [TestCase(RecyclingProcess.Pelletizing)]
        [TestCase(RecyclingProcess.Bagging)]
        public Task AuthenticatedWebHostReadsAndPausesTheRealEncryptedCoordinatorAsync(
            RecyclingProcess? nativeProcess) => VerifyCoordinatorAsync(nativeProcess, outage: false);

        /// <summary>
        /// Channel/keep-alive failure invalidates a retained image even when the session ID still exists.
        /// </summary>
        [Test]
        public Task TransportOutageInvalidatesTheFeedBeforeTheSessionIdIsRemovedAsync() =>
            VerifyCoordinatorAsync(null, outage: true);

        /// <summary>
        /// Composes the actual workstation, trusted server and optional native projection.
        /// </summary>
        private static async Task VerifyCoordinatorAsync(RecyclingProcess? nativeProcess, bool outage)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            await using RecyclingUaEnvironment environment = await RecyclingUaEnvironment.StartAsync(timeout.Token)
                .ConfigureAwait(false);
            IHost? view = null;
            if (nativeProcess.HasValue)
            {
                view = await environment.StartViewAsync(nativeProcess.Value, null, timeout.Token).ConfigureAwait(false);
                while (!environment.Coordinator.Current.Participants.Span.ToArray().Any(participant =>
                    participant.Process == nativeProcess.Value && participant.Initialized))
                {
                    await Task.Delay(50, timeout.Token).ConfigureAwait(false);
                }
            }
            await environment.InitializeRolesAsync(timeout.Token).ConfigureAwait(false);
            int ticks = nativeProcess.HasValue ? 40 : 1;
            for (int tick = 0; tick < ticks; tick++)
            {
                await environment.Coordinator.TickAsync(timeout.Token).ConfigureAwait(false);
            }
            if (view != null)
            {
                await view.Services.GetRequiredService<RecyclingRuntime>()
                    .ApplyProjectionAsync(environment.Coordinator.Current, true, timeout.Token).ConfigureAwait(false);
            }
            string directory = Path.Combine(environment.Root, "workstation");
            Directory.CreateDirectory(directory);
            string bootstrap = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            string bootstrapFile = Path.Combine(directory, "bootstrap");
            await File.WriteAllTextAsync(bootstrapFile, bootstrap, timeout.Token).ConfigureAwait(false);
            var configuration = new Dictionary<string, string?>
            {
                ["RecyclingConsole:Endpoint"] = environment.ServerOptions.EndpointUrl,
                ["RecyclingConsole:LineId"] = environment.ServerOptions.LineId,
                ["RecyclingConsole:Role"] = "SimulatorAdministrator",
                ["RecyclingConsole:BootstrapKeyFile"] = bootstrapFile,
                ["RecyclingConsole:AllowedHost"] = "localhost:8080",
                ["RecyclingConsole:UserCertificatePath"] =
                    Path.Combine(environment.Root, "identities", "administrator"),
                ["RecyclingConsole:UserCertificateSubject"] = "CN=RecyclingTest-administrator",
                ["RecyclingConsole:ApplicationCertificateStore"] = Path.Combine(directory, "pki", "own"),
                ["RecyclingConsole:TrustedPeerStore"] = Path.Combine(directory, "pki", "trusted"),
                ["RecyclingConsole:TrustedIssuerStore"] = Path.Combine(directory, "pki", "issuer"),
                ["RecyclingConsole:RejectedCertificateStore"] = Path.Combine(directory, "pki", "rejected")
            };
            if (view != null)
            {
                configuration["RecyclingConsole:ProcessEndpoints:" + nativeProcess] =
                    view.Services.GetRequiredService<RecyclingHostOptions>().EndpointUrl;
            }
            WebApplication app = OperatorConsoleHost.Build([], builder =>
            {
                builder.Configuration.AddInMemoryCollection(configuration);
                builder.WebHost.UseTestServer();
                builder.Logging.ClearProviders();
            });
            await using var appLifetime = app.ConfigureAwait(false);
            await TrustWorkstationAsync(app, environment, timeout.Token).ConfigureAwait(false);
            await app.StartAsync(timeout.Token).ConfigureAwait(false);
            using HttpClient http = app.GetTestClient();
            http.BaseAddress = new Uri("http://localhost:8080");
            using HttpResponseMessage unauthorized = await http.GetAsync(
                new Uri("/api/state", UriKind.Relative), timeout.Token)
                .ConfigureAwait(false);
            Assert.That(unauthorized.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            using HttpResponseMessage authenticated = await http.GetAsync(
                new Uri("/bootstrap?token=" + bootstrap, UriKind.Relative), timeout.Token).ConfigureAwait(false);
            Assert.That(authenticated.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            string cookie = authenticated.Headers.GetValues("Set-Cookie").Single();
            Assert.That(cookie, Does.Contain("httponly").IgnoreCase);
            Assert.That(cookie, Does.Contain("samesite=strict").IgnoreCase);
            http.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
            string sessionJson = await http.GetStringAsync(
                new Uri("/api/session", UriKind.Relative), timeout.Token).ConfigureAwait(false);
            using JsonDocument browserSession = JsonDocument.Parse(sessionJson);
            string csrf = browserSession.RootElement.GetProperty("csrfToken").GetString()!;
            IOperatorPlantFeed feed = app.Services.GetRequiredService<IOperatorPlantFeed>();
            var updates = feed.Subscribe()!;
            try
            {
                while (!(await updates.Reader.ReadAsync(timeout.Token).ConfigureAwait(false)).Available)
                {
                }
                await environment.Coordinator.TickAsync(timeout.Token).ConfigureAwait(false);
                string expectedTick = environment.Coordinator.Current.Tick.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                while ((await updates.Reader.ReadAsync(timeout.Token).ConfigureAwait(false)).Tick != expectedTick)
                {
                }
            }
            finally
            {
                feed.Unsubscribe(updates);
            }
            Assert.That(feed.Snapshot!.RunId, Is.EqualTo(environment.Coordinator.Current.RunId));
            Assert.That(feed.Snapshot.SourceMass, Is.EqualTo(environment.Coordinator.Current.SourceMass));
            Assert.That(feed.State.Quality, Is.EqualTo("Good"));
            if (outage)
            {
                await VerifyOutageAsync(app, http, environment, csrf, timeout.Token).ConfigureAwait(false);
                await app.StopAsync(timeout.Token).ConfigureAwait(false);
                return;
            }
            string requestId = Guid.NewGuid().ToString("D");
            var request = new CommandRequest(
                requestId, feed.Snapshot.RunId, feed.State.Revision, "Pause");
            string body = JsonSerializer.Serialize(request, ConsoleJsonContext.Default.CommandRequest);
            using var missingCsrf = new HttpRequestMessage(HttpMethod.Post, "/api/commands")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            missingCsrf.Headers.Add("Origin", "http://localhost:8080");
            using HttpResponseMessage refused = await http.SendAsync(missingCsrf, timeout.Token).ConfigureAwait(false);
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(environment.Coordinator.Current.Lifecycle, Is.EqualTo(RunLifecycle.Running));

            using var command = new HttpRequestMessage(HttpMethod.Post, "/api/commands")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            command.Headers.Add("Origin", "http://localhost:8080");
            command.Headers.Add("X-CSRF-Token", csrf);
            using HttpResponseMessage response = await http.SendAsync(command, timeout.Token).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            using JsonDocument outcome = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
            Assert.That(outcome.RootElement.GetProperty("state").GetString(), Is.EqualTo("Accepted"));
            Assert.That(environment.Coordinator.Current.Lifecycle, Is.EqualTo(RunLifecycle.Paused));
            Assert.That(outcome.RootElement.GetProperty("requestId").GetString(), Is.EqualTo(requestId));
            if (nativeProcess.HasValue)
            {
                using HttpResponseMessage inspected = await http.GetAsync(
                    new Uri("/api/processes/" + nativeProcess, UriKind.Relative), timeout.Token).ConfigureAwait(false);
                Assert.That(inspected.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                using JsonDocument native = JsonDocument.Parse(
                    await inspected.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
                Assert.That(native.RootElement.GetProperty("nativeAvailable").GetBoolean(), Is.True,
                    native.RootElement.GetProperty("nativeDetail").GetString());
                Assert.That(native.RootElement.GetProperty("readings").GetArrayLength(), Is.Positive);
                Assert.That(native.RootElement.GetProperty("runId").GetString(),
                    Is.EqualTo(environment.Coordinator.Current.RunId));
                string? expectedOrigin = nativeProcess.Value switch
                {
                    RecyclingProcess.Collection or RecyclingProcess.Bagging => "OPC 40200",
                    RecyclingProcess.Sorting => "OPC 40100",
                    RecyclingProcess.Washing or RecyclingProcess.DensitySeparation => "OPC 40223",
                    RecyclingProcess.Drying => "OPC 40082-6",
                    RecyclingProcess.Extrusion => "OPC 40084-3",
                    RecyclingProcess.MeltFiltration => "OPC 40084-6",
                    RecyclingProcess.Pelletizing => "OPC 40084-8",
                    _ => null
                };
                if (expectedOrigin != null)
                {
                    Assert.That(native.RootElement.GetProperty("readings").EnumerateArray().Any(reading =>
                        reading.GetProperty("origin").GetString()!.Contains(expectedOrigin, StringComparison.Ordinal)),
                        Is.True, "The actual typed companion read must be exercised, not just generic metadata.");
                }
            }
            await app.StopAsync(timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops only the owned test server and observes the public connection notifications and browser feed.
        /// </summary>
        private static async Task VerifyOutageAsync(
            WebApplication app, HttpClient http, RecyclingUaEnvironment environment, string csrf,
            CancellationToken cancellationToken)
        {
            var feed = app.Services.GetRequiredService<ConsoleFeed>();
            ManagedSession session = feed.Client!.Session;
            NodeId sessionId = session.SessionId;
            long tick = feed.Snapshot!.Tick;
            DateTime sourceTime = feed.Snapshot.SourceTimestampUtc;
            session.KeepAliveInterval = 250;
            var retainedSession = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void ChannelFailed(ManagedSession current, ChannelStateChange change)
            {
                if (change.NewState != ChannelState.Ready)
                {
                    retainedSession.TrySetResult(current.Connected && current.SessionId == sessionId);
                }
            }
            void KeepAliveFailed(ISession current, KeepAliveEventArgs args)
            {
                if (ServiceResult.IsBad(args.Status))
                {
                    retainedSession.TrySetResult(current.Connected && current.SessionId == sessionId);
                }
            }
            session.ChannelStateChanged += ChannelFailed;
            session.KeepAlive += KeepAliveFailed;
            var updates = feed.Subscribe()!;
            try
            {
                await environment.ServerHost.StopAsync(cancellationToken).ConfigureAwait(false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(15));
                Assert.That(await retainedSession.Task.WaitAsync(deadline.Token).ConfigureAwait(false), Is.True,
                    "This regression must exercise a failed transport whose session still reports connected.");
                while ((await updates.Reader.ReadAsync(deadline.Token).ConfigureAwait(false)).Available)
                {
                }
                Assert.That(feed.State.Connection, Is.Not.EqualTo("Connected"));
                Assert.That(feed.Snapshot!.Tick, Is.EqualTo(tick));
                Assert.That(feed.Snapshot.SourceTimestampUtc, Is.EqualTo(sourceTime));
                var pause = new CommandRequest(Guid.NewGuid().ToString("D"),
                    feed.Snapshot.RunId, feed.State.Revision, "Pause");
                using var command = new HttpRequestMessage(HttpMethod.Post, "/api/commands")
                {
                    Content = new StringContent(JsonSerializer.Serialize(pause,
                        ConsoleJsonContext.Default.CommandRequest), Encoding.UTF8, "application/json")
                };
                command.Headers.Add("Origin", "http://localhost:8080");
                command.Headers.Add("X-CSRF-Token", csrf);
                using HttpResponseMessage denied = await http.SendAsync(command, cancellationToken)
                    .ConfigureAwait(false);
                Assert.That(denied.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            }
            finally
            {
                session.ChannelStateChanged -= ChannelFailed;
                session.KeepAlive -= KeepAliveFailed;
                feed.Unsubscribe(updates);
            }
        }

        /// <summary>
        /// Provisions explicit peer trust through the stack without disabling hostname or certificate validation.
        /// </summary>
        internal static async Task TrustWorkstationAsync(
            WebApplication app, RecyclingUaEnvironment environment, CancellationToken cancellationToken)
        {
            ApplicationConfiguration configuration = await app.Services
                .GetRequiredService<IOpcUaApplicationConfigurationProvider>().GetAsync(cancellationToken)
                .ConfigureAwait(false);
            ConsoleOptions options = app.Services.GetRequiredService<ConsoleOptions>();
            configuration.SecurityConfiguration.TrustedPeerCertificates = new CertificateTrustList
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = Path.GetFullPath(options.TrustedPeerStore)
            };
            configuration.SecurityConfiguration.TrustedIssuerCertificates = new CertificateTrustList
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = Path.GetFullPath(options.TrustedIssuerStore)
            };
            configuration.SecurityConfiguration.RejectedCertificateStore = new CertificateStoreIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = Path.GetFullPath(options.RejectedCertificateStore)
            };
            await environment.TrustClientApplicationAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
    }
}
