/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.PackML;
using Opc.Ua.PackML.Client;
using Opc.Ua.PackML.Server;
using PackMLSample;
using ServerUnit = Opc.Ua.PackML.Server.PackMLUnit;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.EnableOpcUaRedaction();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
string endpoint = builder.Configuration["endpoint"] ?? "opc.tcp://localhost:62563/PackML";
if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? address) || address.Scheme != "opc.tcp" ||
    !address.IsLoopback)
{
    throw new ArgumentException("The simulated sample requires an absolute loopback opc.tcp endpoint.");
}
string pkiRoot = builder.Configuration["pki-root"] ??
    throw new ArgumentException("Supply --pki-root with an absolute writable certificate-store directory.");
if (!Path.IsPathFullyQualified(pkiRoot))
{
    throw new ArgumentException("--pki-root must be absolute.");
}
ServerUnit? serverUnit = null;
builder.Services.AddSingleton(new SampleEquipment());
var opc = builder.Services.AddOpcUa();
opc.AddServer(options =>
{
    options.ApplicationName = "PackMLSampleServer";
    options.ApplicationUri = "urn:localhost:OPCFoundation:PackMLSampleServer";
    options.ProductUri = "urn:opcfoundation:PackMLSample";
    options.PkiRoot = Path.Combine(pkiRoot, "server");
    // Local simulation only; production applications configure explicit trust.
    options.AutoAcceptUntrustedCertificates = true;
    options.EndpointUrls.Add(endpoint);
}).AddPackML((services, options) =>
{
    options.ConfigureAsync = async (manager, ct) =>
    {
        serverUnit = await manager.Runtime.AddUnitAsync(
            new QualifiedName("Filler", manager.InstanceNamespaceIndex),
            services.GetRequiredService<SampleEquipment>(),
            unit =>
            {
                unit.Modes =
                [
                    new PackMLMode(),
                    new PackMLMode { Value = 2, Name = "Maintenance" },
                    new PackMLMode
                    {
                        Value = 3,
                        Name = "Manual",
                        States =
                        [
                            PackMLStateNumbers.Aborting, PackMLStateNumbers.Aborted, PackMLStateNumbers.Cleared,
                            PackMLStateNumbers.Clearing, PackMLStateNumbers.Stopped
                        ]
                    }
                ];
                unit.TagId = "Simulated filler";
                unit.MaterialCount = 2;
                unit.EnableRemoteCommand = true;
                unit.AdministrationExtents = new PackMLAdministrationExtents
                {
                    Alarms = 4,
                    AlarmHistory = 10,
                    Warnings = 4,
                    StopReasons = 1,
                    ProcessedCounts = 1
                };
                // This sample explicitly permits anonymous read-only administration inspection.
                unit.AdministrationRolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = Opc.Ua.ObjectIds.WellKnownRole_Anonymous,
                        Permissions = (uint)(PermissionType.Browse | PermissionType.Read)
                    }
                ];
            }, cancellationToken: ct).ConfigureAwait(false);
    };
});
opc.AddClient(options =>
{
    options.ApplicationName = "PackMLSampleClient";
    options.ApplicationUri = "urn:localhost:OPCFoundation:PackMLSampleClient";
    options.ProductUri = "urn:opcfoundation:PackMLSample";
    options.PkiRoot = Path.Combine(pkiRoot, "client");
    options.AutoAcceptUntrustedCertificates = true;
}).AddDiscoveryAndConnect(options =>
{
    options.DiscoveryUrl = endpoint;
    options.SecurityMode = MessageSecurityMode.SignAndEncrypt;
    options.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
}).AddPackMLClient();

using IHost host = builder.Build();
using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
await host.StartAsync(lifetime.Token).ConfigureAwait(false);
try
{
    ServerUnit unit = serverUnit ?? throw new InvalidOperationException("The PackML unit was not constructed.");
    Func<CancellationToken, Task<PackMLClient>> connect =
        host.Services.GetRequiredService<Func<CancellationToken, Task<PackMLClient>>>();
    PackMLClient client = await connect(lifetime.Token).ConfigureAwait(false);
    ArrayOf<Opc.Ua.PackML.Client.PackMLUnit> units = await client.DiscoverAsync(
        cancellationToken: lifetime.Token).ConfigureAwait(false);
    NodeId id = units.Span.ToArray().Single().NodeId;
    await client.SetProductAsync(id, [new PackMLProductDataType { ProductID = 42 }], lifetime.Token)
        .ConfigureAwait(false);
    await client.SetParameterAsync(id, [new PackMLDescriptorDataType { ID = 1, Name = "Fill", Value = 250f }],
        lifetime.Token).ConfigureAwait(false);
    await client.SetMachSpeedAsync(id, 120, lifetime.Token).ConfigureAwait(false);
    await unit.RefreshAsync(lifetime.Token).ConfigureAwait(false);
    PackMLStatusSnapshot speed = await client.ReadStatusAsync(id, lifetime.Token).ConfigureAwait(false);
    Console.WriteLine($"Setpoint {speed.MachSpeed.Value}; equipment speed {speed.CurMachSpeed.Value} packages/min.");
    foreach (int mode in new[] { 2, 3, 1 })
    {
        await client.SetUnitModeAsync(id, mode, lifetime.Token).ConfigureAwait(false);
        PackMLStatusSnapshot requested = await client.ReadStatusAsync(id, lifetime.Token).ConfigureAwait(false);
        Console.WriteLine(
            $"Mode request flag {requested.UnitModeRequested.Value}; current {requested.UnitModeCurrent.Value}.");
        await unit.RefreshAsync(lifetime.Token).ConfigureAwait(false);
    }
    await client.CommandAsync(id, PackMLStateCommand.Reset, lifetime.Token).ConfigureAwait(false);
    await CompleteAsync(unit, lifetime.Token).ConfigureAwait(false);
    await client.CommandAsync(id, PackMLStateCommand.Start, lifetime.Token).ConfigureAwait(false);
    await CompleteAsync(unit, lifetime.Token).ConfigureAwait(false);
    try
    {
        await client.SetUnitModeAsync(id, 2, lifetime.Token).ConfigureAwait(false);
        throw new InvalidOperationException("A mode change while Execute should have been rejected.");
    }
    catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadInvalidState)
    {
        Console.WriteLine("Mode change while Execute rejected.");
    }
    await client.SetInterlockAsync(id, 0, true, lifetime.Token).ConfigureAwait(false);
    await client.RemoteCommandAsync(id, [new PackMLRemoteInterfaceDataType { Number = 1, ControlCmdNumber = 7 }],
        lifetime.Token).ConfigureAwait(false);
    await unit.RefreshAsync(lifetime.Token).ConfigureAwait(false);
    await unit.PublishAdministrationAsync(new PackMLAdministrationData
    {
        MachineDesignSpeed = 180,
        ProcessedCounts = [new PackMLCountDataType { ID = 42, Name = "Primary", Count = 5, AccCount = 100 }],
        SourceTimestamp = DateTime.UtcNow,
        StatusCode = StatusCodes.Good
    }, lifetime.Token).ConfigureAwait(false);
    PackMLAdminSnapshot admin = await client.ReadAdminAsync(id, lifetime.Token).ConfigureAwait(false);
    Console.WriteLine($"Processed {admin.ProdProcessedCount.Value[0].Count}; state timers have two dimensions.");
    await client.CommandAsync(id, PackMLStateCommand.Stop, lifetime.Token).ConfigureAwait(false);
    await CompleteAsync(unit, lifetime.Token).ConfigureAwait(false);
}
finally
{
    await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
}

static async Task CompleteAsync(ServerUnit unit, CancellationToken cancellationToken)
{
    uint acting = unit.CurrentState;
    long requestId = unit.PendingRequestId;
    Console.WriteLine($"Request {requestId} accepted in acting state {acting}; equipment has not completed it.");
    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
    ServiceResult result = await unit.CompleteActingStateAsync(requestId, acting, cancellationToken)
        .ConfigureAwait(false);
    if (ServiceResult.IsBad(result))
    {
        throw new ServiceResultException(result);
    }
    Console.WriteLine($"Equipment completed request {requestId}; state {unit.CurrentState}.");
}
