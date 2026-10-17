/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * See http://opcfoundation.org/License/MIT/1.00/ for the complete license.
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
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Recycling.OperatorConsole;

namespace Opc.Ua.Recycling.Operator.Tests.Backend;

/// <summary>
/// Real HTTP middleware security tests over an injected transport boundary.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ConsoleHttpTests
{
    /// <summary>
    /// Starts the actual host pipeline with an externally provisioned launch credential and no UA runtime.
    /// </summary>
    [SetUp]
    public async Task StartHostAsync()
    {
        m_directory = Path.GetFullPath(Path.Combine("artifacts", "operator-http-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(m_directory);
        m_key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string keyFile = Path.Combine(m_directory, "bootstrap");
        await File.WriteAllTextAsync(keyFile, m_key).ConfigureAwait(false);
        m_clock = new ConsoleFeedTests.TestClock();
        m_feed = new Mock<IOperatorPlantFeed>();
        m_feed.SetupGet(item => item.State).Returns(new ConsoleState(
            "Connected", string.Empty, true, m_clock.Now.UtcDateTime, m_clock.Now.UtcDateTime, "Good",
            "line", "run", "test", "config", "3", "1", "1", "Paused", string.Empty,
            new SummaryDto("0", "0", "0", "0", "0", 0, 0), [], [], [], []));
        m_feed.SetupGet(item => item.Snapshot).Returns(new CommittedSnapshot
        {
            RunId = "run", Revision = 3, Tick = 1, Lifecycle = RunLifecycle.Paused
        });
        var configuration = new Dictionary<string, string?>
        {
            ["RecyclingConsole:Endpoint"] = "opc.tcp://localhost:59999",
            ["RecyclingConsole:LineId"] = "line",
            ["RecyclingConsole:Role"] = "Operator",
            ["RecyclingConsole:AllowedHost"] = "localhost:8080",
            ["RecyclingConsole:BootstrapKeyFile"] = keyFile,
            ["RecyclingConsole:UserCertificatePath"] = Path.Combine(m_directory, "users"),
            ["RecyclingConsole:UserCertificateSubject"] = "CN=TestUser",
            ["RecyclingConsole:ApplicationCertificateStore"] = Path.Combine(m_directory, "own"),
            ["RecyclingConsole:TrustedPeerStore"] = Path.Combine(m_directory, "trusted"),
            ["RecyclingConsole:TrustedIssuerStore"] = Path.Combine(m_directory, "issuer"),
            ["RecyclingConsole:RejectedCertificateStore"] = Path.Combine(m_directory, "rejected")
        };
        m_app = OperatorConsoleHost.Build([], builder =>
        {
            builder.Configuration.AddInMemoryCollection(configuration);
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
        }, services =>
        {
            services.RemoveAll<IHostedService>();
            services.Replace(ServiceDescriptor.Singleton<IOperatorPlantFeed>(m_feed.Object));
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(m_clock));
        });
        await m_app.StartAsync().ConfigureAwait(false);
        m_http = m_app.GetTestClient();
        m_http.BaseAddress = new Uri("http://localhost:8080");
    }

    /// <summary>
    /// Releases the actual pipeline and removes only this fixture's owned credential directory.
    /// </summary>
    [TearDown]
    public async Task StopHostAsync()
    {
        m_http?.Dispose();
        if (m_app != null)
        {
            await m_app.DisposeAsync().ConfigureAwait(false);
        }
        if (Directory.Exists(m_directory))
        {
            Directory.Delete(m_directory, recursive: true);
        }
    }

    /// <summary>
    /// The bootstrap shell is reachable, while production data and unknown resources remain protected.
    /// </summary>
    [Test]
    public async Task StaticLoginIsPublicButApiDataRequiresAuthenticationAsync()
    {
        using HttpResponseMessage shell = await m_http.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(false);
        Assert.That(shell.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using HttpResponseMessage api = await m_http.GetAsync(new Uri("/api/state", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.That(api.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        using HttpResponseMessage unknown = await m_http.GetAsync(new Uri("/private-key.pfx", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    /// <summary>
    /// Host-header rebinding is denied before credentials or data are evaluated.
    /// </summary>
    [Test]
    public async Task UnexpectedHostIsRejectedEvenWithAValidSessionAsync()
    {
        await AuthenticateAsync().ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/state");
        request.Headers.Host = "attacker.example";
        using HttpResponseMessage response = await m_http.SendAsync(request).ConfigureAwait(false);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>
    /// Commands need both the exact same origin and an unpredictable per-session anti-CSRF token.
    /// </summary>
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(false, false)]
    public async Task CommandRequiresBothOriginAndCsrfAsync(bool validOrigin, bool validToken)
    {
        string csrf = await AuthenticateAsync().ConfigureAwait(false);
        using var request = Command("EnableProcess", "Sorting");
        request.Headers.Add("Origin", validOrigin ? "http://localhost:8080" : "http://attacker.example");
        request.Headers.Add("X-CSRF-Token", validToken ? csrf : "incorrect");
        using HttpResponseMessage response = await m_http.SendAsync(request).ConfigureAwait(false);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        VerifyNoMutation();
    }

    /// <summary>
    /// Even authenticated anti-CSRF-valid requests cannot call arbitrary UA or process-membership verbs.
    /// </summary>
    [TestCase("Call")]
    [TestCase("Write")]
    [TestCase("Heartbeat")]
    [TestCase("Register")]
    public async Task ArbitraryAndMembershipCommandsAreRejectedAsync(string kind)
    {
        string csrf = await AuthenticateAsync().ConfigureAwait(false);
        using var request = Command(kind, string.Empty);
        request.Headers.Add("Origin", "http://localhost:8080");
        request.Headers.Add("X-CSRF-Token", csrf);
        using HttpResponseMessage response = await m_http.SendAsync(request).ConfigureAwait(false);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        VerifyNoMutation();
    }

    /// <summary>
    /// Browser configuration cannot elevate an operator to simulator administration.
    /// </summary>
    [Test]
    public async Task OperatorCeilingRejectsSimulatorAdministrationAsync()
    {
        string csrf = await AuthenticateAsync().ConfigureAwait(false);
        using var request = Command("Pause", string.Empty);
        request.Headers.Add("Origin", "http://localhost:8080");
        request.Headers.Add("X-CSRF-Token", csrf);
        using HttpResponseMessage response = await m_http.SendAsync(request).ConfigureAwait(false);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        VerifyNoMutation();
    }

    /// <summary>
    /// Unknown fields cannot be silently omitted from an idempotency envelope.
    /// </summary>
    [Test]
    public async Task UnknownCommandFieldIsRejectedInsteadOfIgnoredAsync()
    {
        string csrf = await AuthenticateAsync().ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/commands")
        {
            Content = new StringContent(
                "{\"requestId\":\"" + Guid.NewGuid().ToString("D") +
                "\",\"runId\":\"run\",\"expectedRevision\":\"3\",\"kind\":\"EnableProcess\"," +
                "\"process\":\"Sorting\",\"misspelledControl\":true}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Origin", "http://localhost:8080");
        request.Headers.Add("X-CSRF-Token", csrf);
        using HttpResponseMessage response = await m_http.SendAsync(request).ConfigureAwait(false);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        VerifyNoMutation();
    }

    /// <summary>
    /// A browser cookie cannot extend its server-side thirty-minute lifetime.
    /// </summary>
    [Test]
    public async Task SessionExpiresAgainstTheInjectedClockAsync()
    {
        await AuthenticateAsync().ConfigureAwait(false);
        m_clock.Now = m_clock.Now.AddMinutes(31);
        using HttpResponseMessage response = await m_http.GetAsync(new Uri("/api/state", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    /// <summary>
    /// Exhausted live-consumer admission is an explicit response, not an apparently successful empty stream.
    /// </summary>
    [Test]
    public async Task LiveConsumerLimitReturnsTooManyRequestsAsync()
    {
        await AuthenticateAsync().ConfigureAwait(false);
        using HttpResponseMessage response = await m_http.GetAsync(new Uri("/api/events", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        m_feed.Verify(item => item.Subscribe(), Times.Once);
    }

    /// <summary>
    /// Host shutdown drains a live browser stream without waiting for browser expiry or a forced request abort.
    /// </summary>
    [Test]
    public async Task ApplicationStoppingCompletesLiveEventsBeforeServerShutdownAsync()
    {
        var channel = Channel.CreateUnbounded<ConsoleState>();
        channel.Writer.TryWrite(m_feed.Object.State);
        m_feed.Setup(item => item.Subscribe()).Returns(channel);
        await AuthenticateAsync().ConfigureAwait(false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using HttpResponseMessage response = await m_http.GetAsync(
            new Uri("/api/events", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token)
            .ConfigureAwait(false));
        Assert.That(await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false), Is.EqualTo("event: state"));
        Assert.That(await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false), Does.StartWith("data: "));
        Assert.That(await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false), Is.Empty);

        m_app!.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

        Assert.That(await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false), Is.Null);
        m_feed.Verify(item => item.Unsubscribe(channel), Times.Once);
        VerifyNoMutation();
    }

    /// <summary>
    /// Paging rejects stale runs and out-of-bound cursors rather than reading unrelated history.
    /// </summary>
    [TestCase("material", "run", "0", "0")]
    [TestCase("material", "run", "0", "101")]
    [TestCase("material", "run", "-1", "1")]
    [TestCase("material", "old-run", "0", "1")]
    [TestCase("history", "run", "0", "0")]
    [TestCase("history", "run", "0", "101")]
    [TestCase("history", "run", "-1", "1")]
    [TestCase("history", "old-run", "0", "1")]
    public async Task MaterialAndHistoryRequireMatchingRunAndBoundedPageAsync(
        string route, string run, string after, string limit)
    {
        await AuthenticateAsync().ConfigureAwait(false);
        using HttpResponseMessage response = await m_http.GetAsync(new Uri(
            "/api/" + route + "?runId=" + run + "&after=" + after + "&limit=" + limit, UriKind.Relative))
            .ConfigureAwait(false);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        m_feed.Verify(item => item.ReadHistoryAsync(
            It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// A normal page forwards the exact requested run through the typed history boundary.
    /// </summary>
    [Test]
    public async Task HistoryPassesExpectedRunToTheTypedFeedAsync()
    {
        m_feed.Setup(item => item.ReadHistoryAsync("run", 4, 2, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ArrayOf<MaterialExecution>>([]));
        await AuthenticateAsync().ConfigureAwait(false);
        using JsonDocument history = JsonDocument.Parse(await m_http.GetStringAsync(new Uri(
            "/api/history?runId=run&after=4&limit=2", UriKind.Relative)).ConfigureAwait(false));
        Assert.That(history.RootElement.GetProperty("runId").GetString(), Is.EqualTo("run"));
        Assert.That(history.RootElement.GetProperty("items").GetArrayLength(), Is.Zero);
        m_feed.Verify(item => item.ReadHistoryAsync("run", 4, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// A run mismatch found after a typed read remains a conflict even when the subscription cache is unchanged.
    /// </summary>
    [Test]
    public async Task HistoryRunMismatchReturnsConflictWithRetainedCacheAsync()
    {
        m_feed.Setup(item => item.ReadHistoryAsync("run", 0, 50, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceResultException(StatusCodes.BadInvalidState, "Run changed while reading history."));
        await AuthenticateAsync().ConfigureAwait(false);
        using HttpResponseMessage response = await m_http.GetAsync(new Uri(
            "/api/history?runId=run&after=0&limit=50", UriKind.Relative)).ConfigureAwait(false);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(m_feed.Object.Snapshot!.RunId, Is.EqualTo("run"));
    }

    /// <summary>
    /// Material paging keeps exact large IDs, masses, source zones and a terminating cursor.
    /// </summary>
    [Test]
    public async Task MaterialPagesPreserveExactMassAndSourceGenealogyAsync()
    {
        const long firstId = 9007199254740993;
        var mass = new MaterialMass(9007199254740993, 0, 0, 0, 0, 0);
        var first = new MaterialParcel(firstId, 2, 1, 0, MaterialForm.Flakes,
            [new SourceContribution("source-1", mass, "north")], []);
        var second = first with { Id = firstId + 1 };
        m_feed.SetupGet(item => item.Snapshot).Returns(new CommittedSnapshot
        {
            RunId = "run", Revision = 3, Parcels = [first, second]
        });
        await AuthenticateAsync().ConfigureAwait(false);
        using JsonDocument initial = JsonDocument.Parse(await m_http.GetStringAsync(
            new Uri("/api/material?runId=run&after=0&limit=1", UriKind.Relative)).ConfigureAwait(false));
        JsonElement item = initial.RootElement.GetProperty("items")[0];
        Assert.That(item.GetProperty("id").GetString(), Is.EqualTo("9007199254740993"));
        Assert.That(item.GetProperty("mass").GetProperty("total").GetString(), Is.EqualTo("9007199254740993"));
        Assert.That(item.GetProperty("sources")[0].GetProperty("sourceZone").GetString(), Is.EqualTo("north"));
        Assert.That(initial.RootElement.GetProperty("nextAfter").GetString(), Is.EqualTo("9007199254740993"));

        using JsonDocument last = JsonDocument.Parse(await m_http.GetStringAsync(new Uri(
            "/api/material?runId=run&after=9007199254740993&limit=1", UriKind.Relative)).ConfigureAwait(false));
        Assert.That(last.RootElement.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
        Assert.That(last.RootElement.GetProperty("items")[0].GetProperty("id").GetString(),
            Is.EqualTo("9007199254740994"));
        Assert.That(last.RootElement.GetProperty("nextAfter").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    /// <summary>
    /// Holds beyond the live preview remain reachable, run-scoped and available for explicit disposition.
    /// </summary>
    [Test]
    public async Task HoldPagingReachesRecordsAfterTheLivePreviewAsync()
    {
        var holds = new QualityHold[205];
        for (int index = 0; index < holds.Length; index++)
        {
            holds[index] = new QualityHold(index + 1, 1, 5, "Color limit", index < 200
                ? HoldDisposition.Released : HoldDisposition.Held, string.Empty, 0);
        }
        m_feed.SetupGet(item => item.Snapshot).Returns(new CommittedSnapshot
        {
            RunId = "run", Revision = 3, Holds = holds
        });
        await AuthenticateAsync().ConfigureAwait(false);
        using JsonDocument page = JsonDocument.Parse(await m_http.GetStringAsync(new Uri(
            "/api/holds?runId=run&after=200&limit=2&disposition=Held", UriKind.Relative)).ConfigureAwait(false));
        JsonElement items = page.RootElement.GetProperty("items");
        Assert.That(items.GetArrayLength(), Is.EqualTo(2));
        Assert.That(items[0].GetProperty("id").GetString(), Is.EqualTo("201"));
        Assert.That(items[1].GetProperty("id").GetString(), Is.EqualTo("202"));
        Assert.That(page.RootElement.GetProperty("nextAfter").GetString(), Is.EqualTo("202"));
        using HttpResponseMessage wrongRun = await m_http.GetAsync(new Uri(
            "/api/holds?runId=old-run&after=0&limit=50", UriKind.Relative)).ConfigureAwait(false);
        Assert.That(wrongRun.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    /// <summary>
    /// Performs the actual local launch handshake and obtains its anti-CSRF credential.
    /// </summary>
    private async Task<string> AuthenticateAsync()
    {
        using HttpResponseMessage login = await m_http.GetAsync(new Uri("/bootstrap?token=" + m_key, UriKind.Relative))
            .ConfigureAwait(false);
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        string cookie = login.Headers.GetValues("Set-Cookie").Single();
        Assert.That(cookie, Does.Contain("httponly").IgnoreCase);
        Assert.That(cookie, Does.Contain("samesite=strict").IgnoreCase);
        Assert.That(login.Headers.GetValues("Referrer-Policy").Single(), Is.EqualTo("no-referrer"));
        m_http.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        string json = await m_http.GetStringAsync(new Uri("/api/session", UriKind.Relative)).ConfigureAwait(false);
        SessionDto session = JsonSerializer.Deserialize(json, ConsoleJsonContext.Default.SessionDto)!;
        return session.CsrfToken;
    }

    /// <summary>
    /// Creates a bounded typed browser command without choosing an OPC UA identity.
    /// </summary>
    private static HttpRequestMessage Command(string kind, string process)
    {
        var body = new CommandRequest(Guid.NewGuid().ToString("D"), "run", "3", kind, process);
        return new HttpRequestMessage(HttpMethod.Post, "/api/commands")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, ConsoleJsonContext.Default.CommandRequest),
                Encoding.UTF8, "application/json")
        };
    }

    /// <summary>
    /// Asserts no security rejection crossed the transport seam.
    /// </summary>
    private void VerifyNoMutation() => m_feed.Verify(item => item.ExecuteAsync(
        It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()), Times.Never);

    /// <summary>Owned host instance.</summary>
    private WebApplication? m_app;
    /// <summary>Owned test HTTP client.</summary>
    private HttpClient m_http = null!;
    /// <summary>Injected typed transport boundary.</summary>
    private Mock<IOperatorPlantFeed> m_feed = null!;
    /// <summary>Injected session clock.</summary>
    private ConsoleFeedTests.TestClock m_clock = null!;
    /// <summary>Owned external credential directory.</summary>
    private string m_directory = string.Empty;
    /// <summary>Ephemeral launch credential, never logged.</summary>
    private string m_key = string.Empty;
}
