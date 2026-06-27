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

#nullable enable

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;

namespace RedundantClient
{
    /// <summary>
    /// Entry point for the redundant managed client sample.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Starts the sample.
        /// </summary>
        public static Task<int> Main(string[] args)
        {
            var serverOption = new Option<string[]>("--server", "-s")
            {
                Description = "Server discovery URL. Repeat for each known member of the redundant set."
            };
            var modeOption = new Option<string>("--mode", "-m")
            {
                Description = "Expected failover behavior: cold, warm, hot-a, or hot-b.",
                DefaultValueFactory = _ => "hot-a"
            };
            var noSecurityOption = new Option<bool>("--nosecurity")
            {
                Description = "Select endpoints with MessageSecurityMode.None."
            };
            var autoAcceptOption = new Option<bool>("--autoaccept")
            {
                Description = "Automatically accept untrusted server certificates for sample runs."
            };
            var durationOption = new Option<TimeSpan>("--duration", "-d")
            {
                Description = "How long to monitor before exiting. Use 00:00:00 to run until Ctrl+C.",
                DefaultValueFactory = _ => TimeSpan.FromMinutes(2)
            };
            var pollIntervalOption = new Option<TimeSpan>("--poll-interval")
            {
                Description = "ServiceLevel refresh interval.",
                DefaultValueFactory = _ => TimeSpan.FromSeconds(5)
            };

            var rootCommand = new RootCommand("OPC UA non-transparent redundant managed client sample")
            {
                serverOption,
                modeOption,
                noSecurityOption,
                autoAcceptOption,
                durationOption,
                pollIntervalOption
            };

            rootCommand.SetAction(async (parseResult, cancellationToken) =>
            {
                string[] serverUrls = parseResult.GetValue(serverOption) ?? [];
                if (serverUrls.Length == 0)
                {
                    Console.Error.WriteLine("At least one --server discovery URL is required.");
                    return;
                }

                ClientFailoverMode mode = ParseMode(parseResult.GetValue(modeOption)!);
                bool noSecurity = parseResult.GetValue(noSecurityOption);
                bool autoAccept = parseResult.GetValue(autoAcceptOption);
                TimeSpan duration = parseResult.GetValue(durationOption);
                TimeSpan pollInterval = parseResult.GetValue(pollIntervalOption);

                await RunAsync(
                    serverUrls,
                    mode,
                    noSecurity,
                    autoAccept,
                    duration,
                    pollInterval,
                    cancellationToken).ConfigureAwait(false);
            });

            ParseResult parseResult = rootCommand.Parse(args);
            return parseResult.InvokeAsync(new InvocationConfiguration(), CancellationToken.None);
        }

        private static async Task RunAsync(
            string[] serverUrls,
            ClientFailoverMode mode,
            bool noSecurity,
            bool autoAccept,
            TimeSpan duration,
            TimeSpan pollInterval,
            CancellationToken ct)
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
            });
            using IDisposable? telemetryDisposable = telemetry as IDisposable;
            {
                var application = new ApplicationInstance(telemetry)
                {
                    ApplicationName = kApplicationName,
                    ApplicationType = ApplicationType.Client,
                    ConfigSectionName = kConfigSectionName,
                    CertificatePasswordProvider = new CertificatePasswordProvider([])
                };

                await using (application.ConfigureAwait(false))
                {
                    ApplicationConfiguration configuration = await application
                        .LoadApplicationConfigurationAsync(silent: false, ct: ct)
                        .ConfigureAwait(false);
                    if (autoAccept)
                    {
                        configuration.CertificateManager.AcceptError = AutoAcceptError;
                    }

                    bool haveCertificate = await application
                        .CheckApplicationInstanceCertificatesAsync(silent: false, ct: ct)
                        .ConfigureAwait(false);
                    if (!haveCertificate)
                    {
                        throw new InvalidOperationException("Application instance certificate invalid.");
                    }

                    EndpointDescription selectedEndpoint = await CoreClientUtils
                        .SelectEndpointAsync(
                            configuration,
                            serverUrls[0],
                            useSecurity: !noSecurity,
                            telemetry,
                            ct)
                        .ConfigureAwait(false)
                        ?? throw new InvalidOperationException(
                            $"No endpoint could be selected for '{serverUrls[0]}'.");
                    var configuredEndpoint = new ConfiguredEndpoint(
                        null,
                        selectedEndpoint,
                        EndpointConfiguration.Create(configuration));

                    var resolver = new SeededRedundantServerEndpointResolver(serverUrls, telemetry);
                    var redundancyHandler = new DefaultServerRedundancyHandler(resolver);
                    RedundantManagedClientOptions options = CreateOptions(mode);

                    Console.WriteLine("Connecting to redundant set with initial endpoint {0}", serverUrls[0]);
                    RedundantManagedClient client = await new ManagedSessionBuilder(
                            configuration,
                            telemetry)
                        .UseEndpoint(configuredEndpoint)
                        .WithSessionName(kApplicationName)
                        .WithUserIdentity(new UserIdentity())
                        .WithServerRedundancy(redundancyHandler)
                        .ConnectRedundantAsync(options, ct)
                        .ConfigureAwait(false);
                    await using (client.ConfigureAwait(false))
                    {
                        await LogRedundancyInfoAsync(client, redundancyHandler, ct).ConfigureAwait(false);
                        WarnIfModeDiffers(mode, client.Mode);
                        client.NotificationReceived += OnNotificationReceived;

                        // Ownership of the subscription template transfers to the redundant client,
                        // which disposes it together with the client (see AddSubscriptionAsync remarks).
#pragma warning disable CA2000
                        await client.AddSubscriptionAsync(
                            kSubscriptionKey,
                            CreateCurrentTimeSubscription(client),
                            ct).ConfigureAwait(false);
#pragma warning restore CA2000

                        Console.WriteLine("Monitoring ServerStatus.CurrentTime. Press Ctrl+C to stop.");
                        await MonitorFailoverAsync(
                            client,
                            duration,
                            pollInterval,
                            ct).ConfigureAwait(false);
                    }
                }
            }
        }

        private static Subscription CreateCurrentTimeSubscription(RedundantManagedClient client)
        {
            ManagedSession session = client.CurrentSession
                ?? throw new InvalidOperationException("Redundant client has no active session.");
            var subscription = new Subscription(session.DefaultSubscription)
            {
                DisplayName = "RedundantClient CurrentTime",
                PublishingEnabled = true,
                PublishingInterval = 1000,
                KeepAliveCount = 5,
                LifetimeCount = 0,
                MinLifetimeInterval = 10_000
            };
            var currentTime = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                AttributeId = Attributes.Value,
                DisplayName = "ServerStatus.CurrentTime",
                SamplingInterval = 1000,
                QueueSize = 10,
                DiscardOldest = true
            };
            subscription.AddItem(currentTime);
            return subscription;
        }

        private static async Task MonitorFailoverAsync(
            RedundantManagedClient client,
            TimeSpan duration,
            TimeSpan pollInterval,
            CancellationToken ct)
        {
            DateTime endTime = duration <= TimeSpan.Zero
                ? DateTime.MaxValue
                : DateTime.UtcNow.Add(duration);
            string lastActive = GetActiveServerName(client);
            Console.WriteLine("Active server: {0}", lastActive);

            while (DateTime.UtcNow < endTime && !ct.IsCancellationRequested)
            {
                await Task.Delay(pollInterval, ct).ConfigureAwait(false);
                try
                {
                    await client.RefreshServiceLevelsAsync(ct).ConfigureAwait(false);
                    IRedundantManagedClientSession? active = client.CurrentRedundantSession;
                    if (active != null && !ServiceLevels.IsHealthy(active.ServiceLevel))
                    {
                        Console.WriteLine(
                            "Active server {0} has ServiceLevel {1} ({2}); requesting failover.",
                            GetServerName(active),
                            active.ServiceLevel,
                            ServiceLevels.GetSubrange(active.ServiceLevel));
                        await client.FailoverAsync(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await client.FailoverAsync(ct).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is ServiceResultException or TimeoutException)
                {
                    Console.WriteLine("Refresh failed ({0}); requesting failover.", ex.Message);
                    if (client.CurrentRedundantSession != null)
                    {
                        await client.CurrentRedundantSession.CloseAsync(ct).ConfigureAwait(false);
                    }

                    await client.FailoverAsync(ct).ConfigureAwait(false);
                }

                string activeServer = GetActiveServerName(client);
                if (!string.Equals(activeServer, lastActive, StringComparison.Ordinal))
                {
                    Console.WriteLine("Failover complete. New active server: {0}", activeServer);
                    lastActive = activeServer;
                }
                else if (client.CurrentRedundantSession != null)
                {
                    Console.WriteLine(
                        "Active server remains {0}; ServiceLevel={1} ({2}).",
                        activeServer,
                        client.CurrentRedundantSession.ServiceLevel,
                        ServiceLevels.GetSubrange(client.CurrentRedundantSession.ServiceLevel));
                }
            }
        }

        private static async Task LogRedundancyInfoAsync(
            RedundantManagedClient client,
            DefaultServerRedundancyHandler redundancyHandler,
            CancellationToken ct)
        {
            ManagedSession session = client.CurrentSession
                ?? throw new InvalidOperationException("Redundant client has no active session.");
            ServerRedundancyInfo info = await redundancyHandler
                .FetchRedundancyInfoAsync(session, ct)
                .ConfigureAwait(false);
            Console.WriteLine(
                "Server reports RedundancySupport={0}, ServiceLevel={1} ({2}).",
                info.Mode,
                info.ServiceLevel,
                info.ServiceLevelSubrange);
            for (int ii = 0; ii < info.RedundantServers.Count; ii++)
            {
                RedundantServer server = info.RedundantServers[ii];
                Console.WriteLine(
                    "Peer {0}: uri={1}, state={2}, serviceLevel={3} ({4}), endpoint={5}",
                    ii + 1,
                    server.ServerUri,
                    server.ServerState,
                    server.ServiceLevel,
                    ServiceLevels.GetSubrange(server.ServiceLevel),
                    server.Endpoint?.EndpointUrl?.ToString() ?? "(unresolved)");
            }
        }

        private static void OnNotificationReceived(
            object? sender,
            RedundantManagedClientNotificationEventArgs e)
        {
            Console.WriteLine(
                "Notification from {0}: {1:o} Status={2}",
                e.ServerUri,
                e.Value.GetValue(DateTime.MinValue),
                e.Value.StatusCode);
        }

        private static RedundantManagedClientOptions CreateOptions(ClientFailoverMode mode)
        {
            return new RedundantManagedClientOptions
            {
                HotNotificationMode = mode == ClientFailoverMode.HotB
                    ? HotRedundancyNotificationMode.ReportingMerge
                    : HotRedundancyNotificationMode.ReportingHandoff
            };
        }

        private static void WarnIfModeDiffers(ClientFailoverMode requestedMode, RedundancySupport reportedMode)
        {
            RedundancySupport expected = requestedMode switch
            {
                ClientFailoverMode.Cold => RedundancySupport.Cold,
                ClientFailoverMode.Warm => RedundancySupport.Warm,
                ClientFailoverMode.HotA or ClientFailoverMode.HotB => RedundancySupport.Hot,
                _ => RedundancySupport.Hot
            };
            if (reportedMode != expected)
            {
                Console.WriteLine(
                    "Warning: --mode {0} expects server RedundancySupport={1}, but the server reports {2}.",
                    FormatMode(requestedMode),
                    expected,
                    reportedMode);
            }
        }

        private static ClientFailoverMode ParseMode(string? value)
        {
            return (value ?? "hot-a").Trim().ToLowerInvariant() switch
            {
                "cold" => ClientFailoverMode.Cold,
                "warm" => ClientFailoverMode.Warm,
                "hot-a" or "hota" or "hot" => ClientFailoverMode.HotA,
                "hot-b" or "hotb" => ClientFailoverMode.HotB,
                _ => throw new ArgumentException("Mode must be cold, warm, hot-a, or hot-b.", nameof(value))
            };
        }

        private static string FormatMode(ClientFailoverMode mode)
        {
            return mode switch
            {
                ClientFailoverMode.Cold => "cold",
                ClientFailoverMode.Warm => "warm",
                ClientFailoverMode.HotA => "hot-a",
                ClientFailoverMode.HotB => "hot-b",
                _ => "hot-a"
            };
        }

        private static string GetActiveServerName(RedundantManagedClient client)
        {
            return client.CurrentRedundantSession == null
                ? "(none)"
                : GetServerName(client.CurrentRedundantSession);
        }

        private static string GetServerName(IRedundantManagedClientSession session)
        {
            return session.Endpoint.Description.Server?.ApplicationUri ??
                session.Endpoint.Description.EndpointUrl ??
                session.Endpoint.EndpointUrl?.ToString() ??
                "(unknown)";
        }

        private static bool AutoAcceptError(Certificate certificate, ServiceResult error)
        {
            return true;
        }

        private const string kApplicationName = "RedundantClient";
        private const string kConfigSectionName = "RedundantClient";
        private const string kSubscriptionKey = "server-current-time";
    }
}
