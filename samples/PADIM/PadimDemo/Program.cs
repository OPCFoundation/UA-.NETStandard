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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.PADIM.Client;
using PadimSample;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.EnableOpcUaRedaction();
builder.Logging.AddConsole();

string endpoint = builder.Configuration["endpoint"] ?? "opc.tcp://localhost:62562/PadimDemo";
string pkiRoot = builder.Configuration["pki-root"] ??
    Path.Combine(Environment.CurrentDirectory, "padim-pki");
if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? endpointUri) || endpointUri.Scheme != "opc.tcp")
{
    await Console.Error.WriteLineAsync("--endpoint must be an absolute opc.tcp URL.").ConfigureAwait(false);
    return 2;
}
if (!Path.IsPathFullyQualified(pkiRoot))
{
    await Console.Error.WriteLineAsync("--pki-root must be an absolute path.").ConfigureAwait(false);
    return 2;
}
bool clientMode = ReadBoolean("client");
bool insecure = ReadBoolean("insecure");
bool autoAccept = ReadBoolean("auto-accept");
bool allowDemoWrites = ReadBoolean("allow-demo-writes");
bool observe = ReadBoolean("observe");
if (insecure || autoAccept || allowDemoWrites)
{
    await Console.Error.WriteLineAsync(
        "WARNING: explicit demonstration options weaken endpoint, certificate or write authorization.")
        .ConfigureAwait(false);
}
int runSeconds = 0;
if (builder.Configuration["run-seconds"] is { } duration &&
    (!int.TryParse(duration, NumberStyles.None, CultureInfo.InvariantCulture, out runSeconds) ||
        runSeconds is < 1 or > 3600))
{
    await Console.Error.WriteLineAsync("--run-seconds must be an integer from 1 through 3600.").ConfigureAwait(false);
    return 2;
}
if (clientMode)
{
    builder.Services.AddOpcUa().AddClient(options =>
    {
        options.ApplicationName = "PadimDemoClient";
        options.ApplicationUri = "urn:localhost:OPCFoundation:PadimDemoClient";
        options.ProductUri = "urn:OPCFoundation:PadimDemo";
        options.PkiRoot = pkiRoot;
        options.AutoAcceptUntrustedCertificates = autoAccept;
    }).AddDiscoveryAndConnect(options =>
    {
        options.DiscoveryUrl = endpoint;
        options.SecurityMode = insecure ? MessageSecurityMode.None : MessageSecurityMode.SignAndEncrypt;
        options.SecurityPolicyUri = insecure ? SecurityPolicies.None : SecurityPolicies.Basic256Sha256;
    }).AddSubscriptions().AddPadimClient();
}
else
{
    builder.Services.AddSingleton(new DemonstrationDevice(allowDemoWrites));
    builder.Services.AddOpcUa().AddServer(options =>
    {
        options.ApplicationName = "PadimDemoServer";
        options.ApplicationUri = "urn:localhost:OPCFoundation:PadimDemoServer";
        options.ProductUri = "urn:OPCFoundation:PadimDemo";
        options.PkiRoot = pkiRoot;
        options.AutoAcceptUntrustedCertificates = autoAccept;
        options.IncludeUnsecurePolicyNone = insecure;
        options.EndpointUrls.Add(endpoint);
    }).AddPadim().ConfigurePadim(context =>
        context.GetRequiredService<DemonstrationDevice>().ConfigureAsync(context));
    builder.Services.AddHostedService(services =>
        new DemonstrationRefresh(services.GetRequiredService<DemonstrationDevice>()));
}

using IHost host = builder.Build();
using var lifetime = new CancellationTokenSource();
if (runSeconds != 0)
{
    lifetime.CancelAfter(TimeSpan.FromSeconds(runSeconds));
}
if (!clientMode)
{
    await host.RunAsync(lifetime.Token).ConfigureAwait(false);
    return 0;
}
await host.StartAsync(lifetime.Token).ConfigureAwait(false);
try
{
    PadimClient client = await host.Services.GetRequiredService<PadimClientFactory>()
        .CreateAsync(lifetime.Token).ConfigureAwait(false);
    ArrayOf<PadimEntry> devices = await client.DiscoverDevicesAsync(lifetime.Token).ConfigureAwait(false);
    for (int ii = 0; ii < devices.Count; ii++)
    {
        PadimEntry device = devices[ii];
        Console.WriteLine($"{device.BrowseName}: {device.TypeDefinitionId}");
        await PrintSignalAsync(client, device.NodeId, "Device", lifetime.Token).ConfigureAwait(false);
        ArrayOf<PadimEntry> signals = await client.DiscoverSignalsAsync(device.NodeId, lifetime.Token)
            .ConfigureAwait(false);
        for (int jj = 0; jj < signals.Count; jj++)
        {
            ArrayOf<PadimValue> values = await client.ReadValuesAsync(signals[jj].NodeId, lifetime.Token)
                .ConfigureAwait(false);
            for (int kk = 0; kk < values.Count; kk++)
            {
                PadimValue value = values[kk];
                Console.WriteLine($"  {signals[jj].BrowseName}/{value.BrowseName}: {value.Sample}");
                ArrayOf<ExpandedNodeId> semantics = await client.ReadDictionaryEntriesAsync(
                    value.NodeId, lifetime.Token)
                    .ConfigureAwait(false);
                for (int ll = 0; ll < semantics.Count; ll++)
                {
                    Console.WriteLine($"    IRDI: {semantics[ll]}");
                }
                if (signals[jj].BrowseName.Name == "Temperature" && value.BrowseName.Name == "AnalogSignal")
                {
                    if (observe)
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        timeout.CancelAfter(TimeSpan.FromSeconds(10));
                        var notifications = client.ObserveValueAsync(value.NodeId, timeout.Token)
                            .GetAsyncEnumerator(timeout.Token);
                        await using var notificationLifetime = notifications.ConfigureAwait(false);
                        if (!await notifications.MoveNextAsync().ConfigureAwait(false))
                        {
                            throw new InvalidOperationException("The measurement observation ended without a sample.");
                        }
                        Console.WriteLine($"    Subscription: {notifications.Current.Value}");
                    }
                    if (allowDemoWrites)
                    {
                        PadimProcessValueClient analog = client.ProcessValue(value.NodeId);
                        await analog.SimulationValue.WriteAsync(Variant.From(99f), lifetime.Token)
                            .ConfigureAwait(false);
                        await DemonstrationSimulation.RunAsync(
                            async (enabled, ct) =>
                                await analog.SimulationState.WriteAsync(enabled, ct).ConfigureAwait(false),
                            ct => PrintSignalAsync(client, signals[jj].NodeId, "Simulation", ct),
                            lifetime.Token).ConfigureAwait(false);
                        await PrintSignalAsync(client, signals[jj].NodeId, "Restored acquisition", lifetime.Token)
                            .ConfigureAwait(false);
                    }
                }
            }
            if (allowDemoWrites && signals[jj].BrowseName.Name == "Position")
            {
                PadimValue previousChange = await ReadMemberAsync(
                    client, devices[ii].NodeId, "DateOfLastChange", lifetime.Token).ConfigureAwait(false);
                await client.AutoAdjustPositionerAsync(
                    signals[jj].NodeId, Opc.Ua.PADIM.ExecutionModeEnum.Start, lifetime.Token).ConfigureAwait(false);
                Console.WriteLine("    Positioner Start accepted; waiting for the provider's completion timestamp.");
                DataValue completion = await WaitForChangeAsync(client, previousChange, lifetime.Token)
                    .ConfigureAwait(false);
                Console.WriteLine($"    Positioner completed: {completion}");
                await PrintSignalAsync(client, signals[jj].NodeId, "Completed readback", lifetime.Token)
                    .ConfigureAwait(false);
            }
            if (signals[jj].BrowseName.Name == "Ph")
            {
                NodeId conditions = await FindChildAsync(
                    client, signals[jj].NodeId, "SignalConditionSet", lifetime.Token).ConfigureAwait(false);
                await PrintSignalAsync(client, conditions, "Condition", lifetime.Token).ConfigureAwait(false);
                NodeId calibration = await FindChildAsync(
                    client, signals[jj].NodeId, "Calibration", lifetime.Token).ConfigureAwait(false);
                await PrintSignalAsync(client, calibration, "Calibration", lifetime.Token).ConfigureAwait(false);
                NodeId points = await FindChildAsync(
                    client, calibration, "CalibrationPointSet", lifetime.Token).ConfigureAwait(false);
                NodeId neutral = await FindChildAsync(
                    client, points, "Neutral", lifetime.Token).ConfigureAwait(false);
                await PrintSignalAsync(client, neutral, "Calibration point", lifetime.Token).ConfigureAwait(false);
                if (!allowDemoWrites)
                {
                    continue;
                }
                PadimValue previousCalibration = await ReadMemberAsync(
                    client, calibration, "CalibrationTimestamp", lifetime.Token).ConfigureAwait(false);
                await client.ZeroPointAdjustmentAsync(signals[jj].NodeId, lifetime.Token).ConfigureAwait(false);
                Console.WriteLine("    pH adjustment accepted; waiting for the provider's calibration timestamp.");
                DataValue completion = await WaitForChangeAsync(client, previousCalibration, lifetime.Token)
                    .ConfigureAwait(false);
                Console.WriteLine($"    pH adjustment completed: {completion}");
                await PrintSignalAsync(client, signals[jj].NodeId, "Calibrated readback", lifetime.Token)
                    .ConfigureAwait(false);
            }
        }
    }
}
finally
{
    await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
}
return 0;

static async ValueTask<NodeId> FindChildAsync(
    PadimClient client,
    NodeId parentId,
    string name,
    CancellationToken cancellationToken)
{
    ArrayOf<ReferenceDescription> children = await client.BrowseChildrenAsync(parentId, cancellationToken)
        .ConfigureAwait(false);
    for (int ii = 0; ii < children.Count; ii++)
    {
        if (children[ii].BrowseName.Name == name)
        {
            return ExpandedNodeId.ToNodeId(children[ii].NodeId, client.Session.NamespaceUris);
        }
    }
    throw new InvalidOperationException($"The demonstration member '{name}' is absent.");
}

static async ValueTask<PadimValue> ReadMemberAsync(
    PadimClient client,
    NodeId parentId,
    string name,
    CancellationToken cancellationToken)
{
    ArrayOf<PadimValue> values = await client.ReadValuesAsync(parentId, cancellationToken).ConfigureAwait(false);
    for (int ii = 0; ii < values.Count; ii++)
    {
        if (values[ii].BrowseName.Name == name)
        {
            if (!StatusCode.IsGood(values[ii].Sample.StatusCode) || values[ii].Sample.WrappedValue.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadWaitingForInitialData,
                    $"The demonstration member '{name}' has no healthy acquired value.");
            }
            return values[ii];
        }
    }
    throw new InvalidOperationException($"The demonstration member '{name}' is absent.");
}

static async ValueTask<DataValue> WaitForChangeAsync(
    PadimClient client,
    PadimValue previous,
    CancellationToken cancellationToken)
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(10));
    await foreach (DataValueChange change in client.ObserveValueAsync(previous.NodeId, timeout.Token)
        .ConfigureAwait(false))
    {
        if (StatusCode.IsGood(change.Value.StatusCode) && !change.Value.WrappedValue.IsNull &&
            change.Value.WrappedValue != previous.Sample.WrappedValue)
        {
            return change.Value;
        }
    }
    throw new InvalidOperationException("The completion observation ended without a changed provider timestamp.");
}

static async ValueTask PrintSignalAsync(
    PadimClient client,
    NodeId signalId,
    string label,
    CancellationToken cancellationToken)
{
    ArrayOf<PadimValue> values = await client.ReadValuesAsync(signalId, cancellationToken).ConfigureAwait(false);
    for (int ii = 0; ii < values.Count; ii++)
    {
        Console.WriteLine($"    {label}: {values[ii].BrowseName}: {values[ii].Sample}");
    }
}

bool ReadBoolean(string key)
{
    string? value = builder.Configuration[key];
    if (value == null)
    {
        return false;
    }
    return bool.TryParse(value, out bool parsed)
        ? parsed
        : throw new ArgumentException($"--{key} must be true or false.");
}
