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

using MachinerySample;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Machinery.Server;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = int.TryParse(builder.Configuration["port"], out int configuredPort)
    ? configuredPort
    : 62546;

// The simulation owns the running machine: it drives both OPC 40001-1 state
// machines and publishes the OPC 40001-101 results, and it supplies the ISA-95
// Job Control V2 provider the OPC 40001-3 verbs bind to.
builder.Services.AddSingleton<PressSimulation>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PressSimulation>());
builder.Services.AddSingleton(
    sp => new PressConfigurator(sp.GetRequiredService<PressSimulation>()));

builder.Services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "MachineryServer";
        options.ApplicationUri = "urn:localhost:OPCFoundation:MachineryServer";
        options.ProductUri = "uri:opcfoundation.org:MachineryServer";
        options.AutoAcceptUntrustedCertificates = true;
        options.EndpointUrls.Add($"opc.tcp://localhost:{port}/MachineryServer");
    })
    // AddMachinery claims the Device Integration address space, and - because
    // the Jobs part is selected - the ISA-95 namespaces too. A server that
    // already owns either loads the models into its own manager with
    // nodes.AddMachineryTypeSystem(context, parts) and drives them through
    // ConfigureMachineryFor<TNodeManager>() instead.
    .AddMachinery(options => options.Parts = MachineryParts.All)
    .ConfigureMachinery(async (context, ct) =>
    {
        PressConfigurator configurator = context.GetRequiredService<PressConfigurator>();
        await configurator.ConfigureAsync(context, ct).ConfigureAwait(false);
    });

using IHost host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
