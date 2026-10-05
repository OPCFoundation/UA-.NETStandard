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
using System.IO;
using AmbSample;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.AMB;
using Opc.Ua.Machinery.Server;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = int.TryParse(builder.Configuration["port"], out int configuredPort)
    ? configuredPort
    : 62553;

// What clients write - AssetIds, editable links, links they add, writable
// locations - is kept here and comes back after a restart.
string state = builder.Configuration["state"] ??
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OPC Foundation",
        "AmbServer",
        "state");

// The plant keeps the handles of the assets; the simulation drives them.
builder.Services.AddSingleton<Plant>();
builder.Services.AddHostedService<PlantSimulation>();

builder.Services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "AmbServer";
        options.ApplicationUri = "urn:localhost:OPCFoundation:AmbServer";
        options.ProductUri = "uri:opcfoundation.org:AmbServer";
        // Sample convenience only; never auto-accept untrusted certificates in production.
        options.AutoAcceptUntrustedCertificates = true;
        options.EndpointUrls.Add($"opc.tcp://localhost:{port}/AmbServer");
    })
    // AddMachinery owns the Device Integration address space, so the devices
    // and the machine live in its node manager. The Asset Management Basics
    // manager is a sidecar next to it; it works with AddOpcUaDi, Pumps or
    // Scales the same way.
    .AddMachinery()
    .AddAssetManagement(options =>
    {
        options.UseFileSystemStores(state);

        // Sample convenience only: lets the anonymous sample client change
        // documentation links. By default only authenticated users may.
        options.AuthorizeLinkEdit = _ => true;
    })
    // Runs in the AMB node manager: the location hierarchies the assets are
    // put into below HierarchicalLocations and OperationalLocations, and the
    // dictionary entry the pump is classified with.
    .ConfigureAssetManagement(async context =>
    {
        await context.Assets
            .DefineLocationAsync(AssetLocationKind.Hierarchical, Plant.Line, context.CancellationToken)
            .ConfigureAwait(false);
        await context.Assets
            .DefineLocationAsync(AssetLocationKind.Operational, Plant.Cooling, context.CancellationToken)
            .ConfigureAwait(false);

        // The ECLASS class the cooling pump is classified with, as a
        // dictionary entry object below Server/Dictionaries (OPC 10000-19).
        await context.Assets
            .DefineDictionaryEntryAsync(
                Plant.CentrifugalPumpIrdi,
                new LocalizedText("en", "Centrifugal pump"),
                context.CancellationToken)
            .ConfigureAwait(false);
    })
    .ConfigureMachinery<PressConfigurator>()
    .ConfigureDevicesFor<MachineryNodeManager>(
        context => context.GetRequiredService<Plant>().ConfigureDevicesAsync(context));

using IHost host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
