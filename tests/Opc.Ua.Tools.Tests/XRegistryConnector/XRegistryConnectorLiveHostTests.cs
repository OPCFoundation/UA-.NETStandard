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

#if NET10_0_OR_GREATER
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.XRegistry.Connector;

namespace Opc.Ua.Tools.Tests.XRegistryConnector
{
    [TestFixture]
    [Category("XRegistryConnector")]
    [NonParallelizable]
    public sealed class XRegistryConnectorLiveHostTests
    {
        [Test]
        public Task RunAsyncOpcUaGatewayStartsEncryptedEndpointsRefreshesAndStopsAsync()
        {
            return ExerciseLiveHostAsync(rejectUpstream: false);
        }

        [Test]
        public Task RunAsyncOpcUaGatewayRejectsUnavailableUpstreamAndStopsAsync()
        {
            return ExerciseLiveHostAsync(rejectUpstream: true);
        }

        private static async Task ExerciseLiveHostAsync(bool rejectUpstream)
        {
            string instance = Guid.NewGuid().ToString("N");
            string directory = Path.Combine(Path.GetTempPath(), "xregistry-live-host-" + instance);
            Directory.CreateDirectory(directory);
            try
            {
                using var console = new ConsoleCapture();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                using var stopping = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                int rootReads = 0;
                int modelReads = 0;
                int capabilityReads = 0;
                int modelSourceReads = 0;
                int epoch = 0;
                int refreshedRootSeen = 0;
                int refreshedSnapshots = 0;
                var refreshed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
                builder.Logging.ClearProviders();
                builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
                WebApplication app = builder.Build();
                await using ConfiguredAsyncDisposable appLifetime = app.ConfigureAwait(false);
                app.MapGet("/registry/", async context =>
                {
                    Interlocked.Increment(ref rootReads);
                    int currentEpoch = Volatile.Read(ref epoch);
                    if (currentEpoch != 0)
                    {
                        Volatile.Write(ref refreshedRootSeen, 1);
                    }
                    context.Response.StatusCode = rejectUpstream ? 503 : 200;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(rejectUpstream
                        ? """{"type":"about:blank","detail":"live-host fixture upstream unavailable"}"""
                        : $$"""{"registryid":"live-host","specversion":"1.0-rc4","epoch":{{currentEpoch}}}""",
                        context.RequestAborted).ConfigureAwait(false);
                });
                app.MapGet("/registry/model", async context =>
                {
                    Interlocked.Increment(ref modelReads);
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(k_model, context.RequestAborted).ConfigureAwait(false);
                });
                app.MapGet("/registry/capabilities", async context =>
                {
                    Interlocked.Increment(ref capabilityReads);
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(
                        """
                        {"specversions":["1.0-rc4"],"available":{"modelsource":{"mutable":false}},"mutable":[]}
                        """, context.RequestAborted).ConfigureAwait(false);
                });
                app.MapGet("/registry/modelsource", async context =>
                {
                    Interlocked.Increment(ref modelSourceReads);
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(k_model, context.RequestAborted).ConfigureAwait(false);
                    if (Volatile.Read(ref refreshedRootSeen) != 0 &&
                        Interlocked.Increment(ref refreshedSnapshots) >= 2)
                    {
                        refreshed.TrySetResult(true);
                    }
                });

                Task<int>? host = null;
                int exitCode = -1;
                int port;
                try
                {
                    await app.StartAsync(deadline.Token).ConfigureAwait(false);
                    port = ServerFixtureUtils.GetNextFreeIPPort();
                    Assert.That(port, Is.GreaterThan(0));
                    string profile = "live-" + instance;
                    string applicationUri = "urn:opcfoundation:xregistry:live-host:" + instance;
                    string configuration = Path.Combine(directory, "profile.json");
                    var settingsFile = new JsonObject
                    {
                        ["ApplicationUri"] = applicationUri,
                        ["PkiRoot"] = Path.Combine(directory, "pki"),
                        ["NativeGateway"] = new JsonObject
                        {
                            ["AllowedSubjects"] = new JsonArray("CN=xregistry-live-host-" + instance),
                            ["SpoolDirectory"] = Path.Combine(directory, "spool")
                        },
                        ["Profiles"] = new JsonObject
                        {
                            [profile] = new JsonObject
                            {
                                ["Http"] = new JsonObject { ["IsQualifiedBinding"] = false }
                            }
                        }
                    };
                    await File.WriteAllTextAsync(configuration, settingsFile.ToJsonString(), deadline.Token)
                        .ConfigureAwait(false);
                    var listenAddress = new Uri($"opc.tcp://localhost:{port}");
                    host = XRegistryConnectorHost.RunAsync(new XRegistryConnectorSettings
                    {
                        Command = XRegistryConnectorCommand.OpcUaGateway,
                        ConfigurationFile = configuration,
                        CredentialProfile = profile,
                        HttpRoot = new Uri(app.Urls.Single() + "/registry/"),
                        ListenAddress = listenAddress,
                        AllowLoopbackHttp = true,
                        PollInterval = TimeSpan.FromMilliseconds(100)
                    }, stopping.Token);

                    if (rejectUpstream)
                    {
                        _ = await host.WaitAsync(deadline.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        await WaitForHostSignalAsync(
                            console.FirstOutputLine, host, console, "readiness", deadline.Token).ConfigureAwait(false);
                        using JsonDocument ready = JsonDocument.Parse(
                            await console.FirstOutputLine.ConfigureAwait(false));
                        Assert.Multiple(() =>
                        {
                            Assert.That(ready.RootElement.GetProperty("status").GetString(), Is.EqualTo("ready"));
                            Assert.That(ready.RootElement.GetProperty("mode").GetString(), Is.EqualTo("opcua-gateway"));
                            Assert.That(Volatile.Read(ref modelReads), Is.GreaterThan(0));
                            Assert.That(Volatile.Read(ref capabilityReads), Is.GreaterThan(0));
                            Assert.That(Volatile.Read(ref modelSourceReads), Is.GreaterThan(0));
                            Assert.That(host.IsCompleted, Is.False);
                        });
                        await AssertEncryptedEndpointsAsync(listenAddress, applicationUri, deadline.Token)
                            .ConfigureAwait(false);

                        // ModelSource is snapshot-only. A second post-change snapshot requires
                        // the runner to return from one refresh and begin another pass.
                        Volatile.Write(ref epoch, 1);
                        await WaitForHostSignalAsync(
                            refreshed.Task, host, console, "projection refresh", deadline.Token).ConfigureAwait(false);
                        Assert.That(host.IsCompleted, Is.False);
                    }
                }
                finally
                {
                    try
                    {
                        await stopping.CancelAsync().ConfigureAwait(false);
                        if (host is not null)
                        {
                            exitCode = await host.WaitAsync(TimeSpan.FromSeconds(45)).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        await app.StopAsync(shutdown.Token).ConfigureAwait(false);
                    }
                }

                Assert.Multiple(() =>
                {
                    Assert.That(deadline.IsCancellationRequested, Is.False);
                    Assert.That(exitCode, Is.EqualTo(rejectUpstream ? 1 : 0), console.Error);
                    Assert.That(rootReads, Is.GreaterThan(0));
                });
                if (rejectUpstream)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(console.Output, Is.Empty);
                        Assert.That(console.Error, Is.Not.Empty);
                        Assert.That(modelReads, Is.Zero);
                        Assert.That(capabilityReads, Is.Zero);
                        Assert.That(modelSourceReads, Is.Zero);
                    });
                }
                else
                {
                    Assert.That(console.Error, Does.Not.Contain("xRegistry bridge health or repair failed."));
                    Assert.That(refreshedSnapshots, Is.GreaterThanOrEqualTo(2));
                    Assert.That(Directory.EnumerateFiles(
                        Path.Combine(directory, "pki"), "*", SearchOption.AllDirectories), Is.Not.Empty);
                }
                AssertListenerReleased(port);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
            Assert.That(Directory.Exists(directory), Is.False);
        }

        private static async Task WaitForHostSignalAsync(
            Task signal, Task<int> host, ConsoleCapture console, string milestone, CancellationToken cancellationToken)
        {
            Task completed = await Task.WhenAny(signal, host).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (completed == host)
            {
                int exitCode = await host.ConfigureAwait(false);
                Assert.Fail($"Native host exited with code {exitCode} before {milestone}: {console.Error}");
            }
            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task AssertEncryptedEndpointsAsync(
            Uri listenAddress, string applicationUri, CancellationToken cancellationToken)
        {
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(static _ => { });
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 10_000;
            using DiscoveryClient discovery = await DiscoveryClient.CreateAsync(
                listenAddress, configuration, telemetry, ct: cancellationToken).ConfigureAwait(false);
            ArrayOf<EndpointDescription> endpoints = await discovery.GetEndpointsAsync(
                default, cancellationToken).ConfigureAwait(false);
            Assert.That(endpoints, Is.Not.Empty);
            Assert.That(endpoints.ToList().Select(endpoint => endpoint.SecurityPolicyUri),
                Does.Contain(SecurityPolicies.Basic256Sha256));
            foreach (EndpointDescription endpoint in endpoints)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(new Uri(endpoint.EndpointUrl ??
                        throw new AssertionException("The endpoint omitted its URL.")).Port,
                        Is.EqualTo(listenAddress.Port));
                    Assert.That(endpoint.Server.ApplicationUri, Is.EqualTo(applicationUri));
                    Assert.That(endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                    Assert.That(endpoint.SecurityPolicyUri, Is.Not.EqualTo(SecurityPolicies.None));
                    Assert.That(endpoint.ServerCertificate.IsEmpty, Is.False);
                    Assert.That(endpoint.UserIdentityTokens, Is.Not.Empty);
                    Assert.That(endpoint.UserIdentityTokens.ToList().Select(policy => policy.TokenType),
                        Is.All.EqualTo(UserTokenType.Certificate));
                });
            }
        }

        private static void AssertListenerReleased(int port)
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                ExclusiveAddressUse = true
            };
            Assert.DoesNotThrow(() =>
            {
                listener.Bind(new IPEndPoint(IPAddress.Loopback, port));
                listener.Listen(1);
            }, "RunAsync must release its native listener before returning.");
        }

        private sealed class ConsoleCapture : IDisposable
        {
            public ConsoleCapture()
            {
                m_originalOutput = Console.Out;
                m_originalError = Console.Error;
                Console.SetOut(m_output);
                Console.SetError(m_error);
            }

            public Task<string> FirstOutputLine => m_output.FirstLine;

            public string Output => m_output.ToString();

            public string Error => m_error.ToString();

            public void Dispose()
            {
                Console.SetOut(m_originalOutput);
                Console.SetError(m_originalError);
                m_output.Dispose();
                m_error.Dispose();
            }

            private readonly TextWriter m_originalOutput;
            private readonly TextWriter m_originalError;
            private readonly ReadinessWriter m_output = new();
            private readonly StringWriter m_error = new(CultureInfo.InvariantCulture);
        }

        private sealed class ReadinessWriter : StringWriter
        {
            public ReadinessWriter()
                : base(CultureInfo.InvariantCulture)
            {
            }

            public Task<string> FirstLine => m_firstLine.Task;

            public override void WriteLine(string? value)
            {
                base.WriteLine(value);
                if (value is not null)
                {
                    m_firstLine.TrySetResult(value);
                }
            }

            public override Task WriteLineAsync(string? value)
            {
                WriteLine(value);
                return Task.CompletedTask;
            }

            private readonly TaskCompletionSource<string> m_firstLine =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private const string k_model = """{"groups":{}}""";
    }
}
#endif
