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
 * This notice and this permission notice shall be
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Alarms;
using Opc.Ua.Client.Subscriptions;

try
{
    (string discoveryUrl, bool insecure, bool autoAccept) = ParseArguments(args);

    Console.WriteLine("OPC UA Minimal Console Client");
    Console.WriteLine("OPC UA library: {0}", Utils.GetAssemblyBuildNumber());
    Console.WriteLine($"Discovery URL: {discoveryUrl}");
    if (insecure)
    {
        Console.Error.WriteLine(
            "WARNING: --insecure selects an endpoint without message security.");
    }
    if (autoAccept)
    {
        Console.Error.WriteLine(
            "WARNING: --auto-accept trusts untrusted server certificates.");
    }
    Console.WriteLine();

    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);

    builder.Services
        .AddOpcUa()
        .AddOpcTcpTransport()
        .AddClient(options =>
        {
            options.ApplicationName = "MinimalClient";
            options.ApplicationUri = "urn:localhost:OPCFoundation:MinimalClient";
            options.ProductUri = "uri:opcfoundation.org:MinimalClient";
            options.AutoAcceptUntrustedCertificates = autoAccept;
            options.Session = new ManagedSessionOptions
            {
                SessionName = "MinimalClient",
                SessionTimeout = TimeSpan.FromSeconds(60),
            };
        })
        .AddDiscoveryAndConnect(options =>
        {
            options.DiscoveryUrl = discoveryUrl;
            options.SecurityMode = insecure
                ? MessageSecurityMode.None
                : MessageSecurityMode.SignAndEncrypt;
            options.SecurityPolicyUri = insecure
                ? SecurityPolicies.None
                : SecurityPolicies.Basic256Sha256;
        })
        .AddSubscriptions()
        .AddAlarms();

    using IHost host = builder.Build();
    await host.StartAsync(CancellationToken.None).ConfigureAwait(false);
    try
    {
        await RunClientAsync(host.Services).ConfigureAwait(false);
    }
    finally
    {
        await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}

static (string DiscoveryUrl, bool Insecure, bool AutoAccept) ParseArguments(
    string[] arguments)
{
    const string defaultDiscoveryUrl =
        "opc.tcp://localhost:62541/MinimalBoilerServer";
    string? discoveryUrl = null;
    bool insecure = false;
    bool autoAccept = false;

    foreach (string argument in arguments)
    {
        switch (argument)
        {
            case "--insecure":
                insecure = true;
                break;
            case "--auto-accept":
                autoAccept = true;
                break;
            default:
                if (argument.StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Unknown option '{argument}'.",
                        nameof(arguments));
                }
                if (discoveryUrl != null)
                {
                    throw new ArgumentException(
                        "Only one discovery URL can be specified.",
                        nameof(arguments));
                }
                discoveryUrl = argument;
                break;
        }
    }

    return (discoveryUrl ?? defaultDiscoveryUrl, insecure, autoAccept);
}

static async Task RunClientAsync(IServiceProvider services)
{
    Func<CancellationToken, Task<ManagedSession>> connect =
        services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
    CancellationToken cancellationToken = CancellationToken.None;

    Console.WriteLine("Discovering a matching endpoint and creating a session...");
    ManagedSession session = await connect(cancellationToken).ConfigureAwait(false);

    await using (session)
    {
        ConfiguredEndpoint endpoint = session.ConfiguredEndpoint ??
            throw new InvalidOperationException("The connected session has no configured endpoint.");
        Console.WriteLine($"Using endpoint: {endpoint.EndpointUrl}");
        Console.WriteLine($"Security mode: {endpoint.Description.SecurityMode}");
        Console.WriteLine($"Security policy: {endpoint.Description.SecurityPolicyUri}");
        Console.WriteLine("Connected!");
        Console.WriteLine();

        AlarmClient alarmClient = services.GetRequiredService<AlarmClientFactory>().Create(session);
        Console.WriteLine($"A&C client ready: {alarmClient.GetType().Name}");

        var handler = new ConsoleSubscriptionHandler();
        ISubscription subscription = session.AddSubscription(
            handler,
            options => options with
            {
                PublishingInterval = TimeSpan.FromSeconds(1),
                KeepAliveCount = 10,
                LifetimeCount = 100,
                PublishingEnabled = true,
            });
        await using (subscription.ConfigureAwait(false))
        {
            if (!subscription.TryAddMonitoredItem(
                    "ServerStatus.CurrentTime",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    options => options with
                    {
                        SamplingInterval = TimeSpan.FromSeconds(1),
                        QueueSize = 1,
                    },
                    out _))
            {
                throw new InvalidOperationException("Could not add the server-time monitored item.");
            }

            Console.WriteLine();
            Console.WriteLine("Browsing Objects folder...");
            var browser = new Browser(session)
            {
                BrowseDirection = BrowseDirection.Forward,
                NodeClassMask = (uint)NodeClass.Object | (uint)NodeClass.Variable,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
            };

            ArrayOf<ReferenceDescription> references = await browser.BrowseAsync(
                ObjectIds.ObjectsFolder,
                cancellationToken).ConfigureAwait(false);

            Console.WriteLine($"Found {references.Count} references");
            foreach (ReferenceDescription reference in references)
            {
                Console.WriteLine($"  - {reference.DisplayName} ({reference.NodeClass})");
            }

            Console.WriteLine();

            Console.WriteLine("Reading ServerStatus.CurrentTime...");
            ReadResponse readResponse = await session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new ReadValueId
                    {
                        NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                        AttributeId = Attributes.Value,
                    },
                },
                cancellationToken).ConfigureAwait(false);

            if (readResponse.Results.Count > 0)
            {
                DataValue dataValue = readResponse.Results[0];
                if (!StatusCode.IsBad(dataValue.StatusCode))
                {
                    Console.WriteLine($"Server time: {dataValue.WrappedValue}");
                }
                else
                {
                    Console.WriteLine(
                        $"Failed to read server time: {dataValue.StatusCode}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("Disconnecting...");
        }
    }

    Console.WriteLine("Done");
}

internal sealed class ConsoleSubscriptionHandler : ISubscriptionNotificationHandler
{
    public ValueTask OnDataChangeNotificationAsync(
        ISubscription subscription,
        uint sequenceNumber,
        DateTime publishTime,
        ReadOnlyMemory<DataValueChange> notification,
        PublishState publishStateMask,
        IReadOnlyList<string> stringTable)
    {
        foreach (DataValueChange change in notification.Span)
        {
            Console.WriteLine($"Subscription value: {change.Value.WrappedValue}");
        }
        return default;
    }

    public ValueTask OnEventDataNotificationAsync(
        ISubscription subscription,
        uint sequenceNumber,
        DateTime publishTime,
        ReadOnlyMemory<EventNotification> notification,
        PublishState publishStateMask,
        IReadOnlyList<string> stringTable)
    {
        Console.WriteLine($"Received {notification.Length} A&C event notification(s).");
        return default;
    }

    public ValueTask OnKeepAliveNotificationAsync(
        ISubscription subscription,
        uint sequenceNumber,
        DateTime publishTime,
        PublishState publishStateMask)
    {
        return default;
    }

    public ValueTask OnSubscriptionStateChangedAsync(
        ISubscription subscription,
        Opc.Ua.Client.Subscriptions.SubscriptionState state,
        PublishState publishStateMask,
        CancellationToken ct = default)
    {
        Console.WriteLine($"Subscription state: {state}");
        return default;
    }
}
