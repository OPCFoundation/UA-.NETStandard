/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Recycling.OperatorConsole;
using Opc.Ua.Recycling.Samples.Tests;

namespace Opc.Ua.Recycling.Operator.Tests.Backend
{
    /// <summary>
    /// Explicit executable-level qualification, separate from managed host or browser fixture tests.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class NativeConsoleSmokeTests
    {
        /// <summary>
        /// The actual native executable authenticates HTTP, streams encrypted UA data and submits a permitted command.
        /// </summary>
        [Test]
        [Category("NativeSmoke")]
        [Explicit("Set OPCUA_RECYCLING_OPERATOR_NATIVE_EXE to the actual published NativeAOT executable.")]
        public async Task NativeConsoleAuthenticatesStreamsAndControlsTheRealCoordinatorAsync()
        {
            string executable = Environment.GetEnvironmentVariable("OPCUA_RECYCLING_OPERATOR_NATIVE_EXE")
                ?? throw new InvalidOperationException("The native executable path is required.");
            Assert.That(File.Exists(executable), Is.True);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await using RecyclingUaEnvironment environment = await RecyclingUaEnvironment.StartAsync(timeout.Token)
                .ConfigureAwait(false);
            await environment.InitializeRolesAsync(timeout.Token).ConfigureAwait(false);
            await environment.Coordinator.TickAsync(timeout.Token).ConfigureAwait(false);
            string root = Path.Combine(environment.Root, "native-workstation");
            Directory.CreateDirectory(root);
            string key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            string keyFile = Path.Combine(root, "bootstrap");
            await File.WriteAllTextAsync(keyFile, key, timeout.Token).ConfigureAwait(false);
            int port;
            using (var listener = new TcpListener(IPAddress.Loopback, 0))
            {
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            var origin = new Uri("http://localhost:" + port);
            var configuration = new Dictionary<string, string?>
            {
                ["RecyclingConsole:Endpoint"] = environment.ServerOptions.EndpointUrl,
                ["RecyclingConsole:LineId"] = environment.ServerOptions.LineId,
                ["RecyclingConsole:Role"] = "SimulatorAdministrator",
                ["RecyclingConsole:AllowedHost"] = origin.Authority,
                ["RecyclingConsole:BootstrapKeyFile"] = keyFile,
                ["RecyclingConsole:UserCertificatePath"] =
                    Path.Combine(environment.Root, "identities", "administrator"),
                ["RecyclingConsole:UserCertificateSubject"] = "CN=RecyclingTest-administrator",
                ["RecyclingConsole:ApplicationCertificateStore"] = Path.Combine(root, "pki", "own"),
                ["RecyclingConsole:TrustedPeerStore"] = Path.Combine(root, "pki", "trusted"),
                ["RecyclingConsole:TrustedIssuerStore"] = Path.Combine(root, "pki", "issuer"),
                ["RecyclingConsole:RejectedCertificateStore"] = Path.Combine(root, "pki", "rejected")
            };
            WebApplication provisioner = OperatorConsoleHost.Build([], builder =>
            {
                builder.Configuration.AddInMemoryCollection(configuration);
                builder.Logging.ClearProviders();
            });
            await using (provisioner.ConfigureAwait(false))
            {
                await ConsoleUaIntegrationTests.TrustWorkstationAsync(provisioner, environment, timeout.Token)
                    .ConfigureAwait(false);
            }
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(executable)
                {
                    WorkingDirectory = Path.GetDirectoryName(executable)!,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            foreach ((string name, string? value) in configuration)
            {
                process.StartInfo.Environment[name.Replace(":", "__", StringComparison.Ordinal)] = value;
            }
            Assert.That(process.Start(), Is.True);
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                using var http = new HttpClient(new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    CheckCertificateRevocationList = true
                })
                {
                    BaseAddress = origin
                };
                HttpResponseMessage? ready = null;
                while (ready == null)
                {
                    Assert.That(process.HasExited, Is.False, "The native console exited before listening.");
                    try
                    {
                        ready = await http.GetAsync(new Uri("/api/state", UriKind.Relative), timeout.Token)
                            .ConfigureAwait(false);
                    }
                    catch (HttpRequestException) when (!timeout.IsCancellationRequested)
                    {
                        await Task.Delay(100, timeout.Token).ConfigureAwait(false);
                    }
                }
                using (ready)
                {
                    Assert.That(ready.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                }
                using HttpResponseMessage login = await http.GetAsync(
                    new Uri("/bootstrap?token=" + key, UriKind.Relative), timeout.Token).ConfigureAwait(false);
                Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
                string cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
                http.DefaultRequestHeaders.Add("Cookie", cookie);
                using HttpResponseMessage shell = await http.GetAsync(
                    new Uri("/", UriKind.Relative), timeout.Token).ConfigureAwait(false);
                Assert.That(shell.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(await shell.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false),
                    Does.Contain("data-testid=\"plant-schematic\""));
                using JsonDocument session = JsonDocument.Parse(await http.GetStringAsync(
                    new Uri("/api/session", UriKind.Relative), timeout.Token).ConfigureAwait(false));
                string csrf = session.RootElement.GetProperty("csrfToken").GetString()!;
                using HttpResponseMessage events = await http.GetAsync(
                    new Uri("/api/events", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                Assert.That(events.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                using var stream = new StreamReader(await events.Content.ReadAsStreamAsync(timeout.Token)
                    .ConfigureAwait(false));
                string runId = string.Empty;
                string revision = string.Empty;
                while (runId.Length == 0)
                {
                    string? line = await stream.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(line, Is.Not.Null);
                    if (!line!.StartsWith("data: ", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    using JsonDocument observed = JsonDocument.Parse(line["data: ".Length..]);
                    if (observed.RootElement.GetProperty("available").GetBoolean())
                    {
                        runId = observed.RootElement.GetProperty("runId").GetString()!;
                        revision = observed.RootElement.GetProperty("revision").GetString()!;
                        Assert.That(observed.RootElement.GetProperty("quality").GetString(), Is.EqualTo("Good"));
                    }
                }
                Assert.That(runId, Is.EqualTo(environment.Coordinator.Current.RunId));
                var pause = new CommandRequest(Guid.NewGuid().ToString("D"), runId, revision, "Pause");
                using var command = new HttpRequestMessage(HttpMethod.Post, "/api/commands")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(pause, ConsoleJsonContext.Default.CommandRequest),
                        Encoding.UTF8, "application/json")
                };
                command.Headers.Add("Origin", origin.GetLeftPart(UriPartial.Authority));
                command.Headers.Add("X-CSRF-Token", csrf);
                using HttpResponseMessage result = await http.SendAsync(command, timeout.Token).ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                using JsonDocument receipt = JsonDocument.Parse(
                    await result.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
                Assert.That(receipt.RootElement.GetProperty("state").GetString(), Is.EqualTo("Accepted"));
                Assert.That(environment.Coordinator.Current.Lifecycle, Is.EqualTo(RunLifecycle.Paused));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                string diagnostic = await error.ConfigureAwait(false);
                _ = await output.ConfigureAwait(false);
                Assert.That(diagnostic, Does.Not.Contain("Unhandled exception"));
            }
        }
    }
}
