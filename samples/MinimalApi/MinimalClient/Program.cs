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
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Alarms;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Samples;

Option<bool> autoAcceptOption = SampleCommandLine.CreateAutoAcceptOption("--autoaccept", "-a");
var insecureOption = new Option<bool>("--insecure")
{
    Description = "Select SecurityPolicy None (no message signing or encryption); does not trust certificates."
};
var discoveryUrlArgument = new Argument<string>("discovery-url")
{
    Description = "The OPC UA discovery endpoint URL.",
    Arity = ArgumentArity.ZeroOrOne,
    DefaultValueFactory = _ => "opc.tcp://localhost:62541/MinimalBoilerServer"
};
discoveryUrlArgument.Validators.Add(result =>
{
    if (!Uri.TryCreate(result.GetValueOrDefault<string>(), UriKind.Absolute, out Uri? uri) ||
        string.IsNullOrEmpty(uri.Host))
    {
        result.AddError("The discovery URL must be an absolute endpoint URL, such as opc.tcp://localhost:62541.");
    }
});
var command = new RootCommand("OPC UA Minimal Client: trusted certificates and SignAndEncrypt by default.")
{
    autoAcceptOption,
    insecureOption,
    discoveryUrlArgument
};
command.SetAction(async (result, cancellationToken) =>
{
    string discoveryUrl = result.GetValue(discoveryUrlArgument)!;
    bool insecure = result.GetValue(insecureOption);
    bool autoAccept = result.GetValue(autoAcceptOption);

    Console.WriteLine("OPC UA Minimal Console Client");
    Console.WriteLine("OPC UA library: {0}", Utils.GetAssemblyBuildNumber());
    Console.WriteLine($"Discovery URL: {discoveryUrl}");
    SampleCommandLine.WriteSecurityWarnings(Console.Error, autoAccept, insecure, "server", "--insecure");
    Console.WriteLine();

    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);

    builder.Services
        .AddOpcUa()
        .AddClient(options =>
        {
            const string applicationName = "MinimalClient";
            options.ApplicationName = applicationName;
            options.ApplicationUri = "urn:localhost:OPCFoundation:MinimalClient";
            options.ProductUri = "uri:opcfoundation.org:MinimalClient";
            options.PkiRoot = Path.Combine(
                Path.GetTempPath(),
                "OPC Foundation",
                applicationName,
                "pki");
            options.AutoAcceptUntrustedCertificates = autoAccept;
            options.RejectSHA1SignedCertificates = true;
            options.MinimumCertificateKeySize = 2048;
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
    await host.StartAsync(cancellationToken).ConfigureAwait(false);
    try
    {
        await RunClientAsync(host.Services, cancellationToken).ConfigureAwait(false);
    }
    finally
    {
        await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }
    return 0;
});

return await SampleCommandLine.InvokeAsync(command, args, Console.Out, Console.Error).ConfigureAwait(false);

static async Task RunClientAsync(IServiceProvider services, CancellationToken cancellationToken)
{
    Func<CancellationToken, Task<ManagedSession>> connect =
        services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
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

/// <summary>
/// Writes sample subscription values, event counts, and state changes to the console while ignoring keep-alives.
/// </summary>
internal sealed class ConsoleSubscriptionHandler : ISubscriptionNotificationHandler
{
    /// <summary>
    /// Writes each changed value in the received data-change notification to the console.
    /// </summary>
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

    /// <summary>
    /// Reports the number of received alarm and condition events without decoding their fields.
    /// </summary>
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

    /// <summary>
    /// Completes a keep-alive callback without producing console output.
    /// </summary>
    public ValueTask OnKeepAliveNotificationAsync(
        ISubscription subscription,
        uint sequenceNumber,
        DateTime publishTime,
        PublishState publishStateMask)
    {
        return default;
    }

    /// <summary>
    /// Writes the subscription's new lifecycle state to the console.
    /// </summary>
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
