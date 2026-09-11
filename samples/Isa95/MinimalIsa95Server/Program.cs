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
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MinimalIsa95Server;
using Opc.Ua;
using Opc.Ua.ISA95;
using Opc.Ua.Samples;
using Isa95GeoSpatialLocationBinding = Opc.Ua.ISA95.Server.Builders.Isa95GeoSpatialLocationBinding;

Option<bool> autoAcceptOption = SampleCommandLine.CreateAutoAcceptOption("--autoaccept", "-a");
Option<string> portOption = SampleCommandLine.CreateInt32ConfigurationOption(
    "--port", "Endpoint port (default 62545).", 1, 65535);
Argument<string[]> configurationArgument = SampleCommandLine.CreateConfigurationArgument();
(string[] sampleArguments, string[] forwardedArguments) = SampleCommandLine.SplitHostArguments(args);
var command = new RootCommand("OPC UA Minimal ISA-95 Server: trusted certificates and secure endpoints.")
{
    autoAcceptOption,
    portOption,
    configurationArgument
};
command.Validators.Add(result =>
{
    if (SampleCommandLine.GetHostArgumentError(forwardedArguments) is string error)
    {
        result.AddError(error);
    }
});
ParseResult? parsed = null;
command.SetAction(result => parsed = result);
int exitCode = await SampleCommandLine.InvokeAsync(
    command, sampleArguments, Console.Out, Console.Error).ConfigureAwait(false);
if (parsed is null)
{
    return exitCode;
}
bool autoAccept = parsed.GetValue(autoAcceptOption);
SampleCommandLine.WriteSecurityWarnings(Console.Error, autoAccept, false, "client", string.Empty);
HostApplicationBuilder builder = Host.CreateApplicationBuilder(
    SampleCommandLine.GetHostArguments(parsed, forwardedArguments, configurationArgument, portOption));
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = 62545;
if (builder.Configuration["port"] is string configuredPort &&
    (!int.TryParse(configuredPort, out port) || port is < 1 or > 65535))
{
    Console.Error.WriteLine("The configured port must be an integer between 1 and 65535. Use --help.");
    return 1;
}
Isa95GeoSpatialLocationBinding? locationBinding = null;
const string PlantSourceId = "plant";
using var locationProvider = new InMemoryGeoLocationProvider();
locationProvider.Update(
    PlantSourceId,
    new GeoPosition(47.3769, 8.5417, EpsgCode: 4326));

builder.Services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "MinimalIsa95Server";
        options.ApplicationUri = "urn:localhost:OPCFoundation:MinimalIsa95Server";
        options.ProductUri = "uri:opcfoundation.org:MinimalIsa95Server";
        options.AutoAcceptUntrustedCertificates = autoAccept;
        options.IncludeUnsecurePolicyNone = false;
        options.EndpointUrls.Add(
            $"opc.tcp://localhost:{port}/MinimalIsa95Server");
    })
    .AddIsa95Server()
    .ConfigureModel(async (model, ct) =>
    {
        PersonnelClassState operators =
            await model.CreatePersonnelClassAsync(model.Root, "Operators", ct)
                .ConfigureAwait(false);
        PersonState operatorOne =
            await model.CreatePersonAsync(model.Root, "Operator-1", ct)
                .ConfigureAwait(false);
        model.DefinedByPersonnelClass(operatorOne, operators);

        EquipmentClassState reactorClass =
            await model.CreateEquipmentClassAsync(model.Root, "ReactorClass", ct)
                .ConfigureAwait(false);
        EquipmentState reactor =
            await model.CreateEquipmentAsync(model.Root, "Reactor-1", ct)
                .ConfigureAwait(false);
        model.DefinedByEquipmentClass(reactor, reactorClass);

        PhysicalAssetClassState vesselClass =
            await model.CreatePhysicalAssetClassAsync(model.Root, "VesselClass", ct)
                .ConfigureAwait(false);
        PhysicalAssetState vessel =
            await model.CreatePhysicalAssetAsync(model.Root, "Vessel-1", ct)
                .ConfigureAwait(false);
        model.DefinedByPhysicalAssetClass(vessel, vesselClass);

        MaterialClassState materialClass =
            await model.CreateMaterialClassAsync(model.Root, "Feedstock", ct)
                .ConfigureAwait(false);
        MaterialDefinitionState materialDefinition =
            await model.CreateMaterialDefinitionAsync(
                model.Root,
                "Feedstock-Grade-A",
                ct).ConfigureAwait(false);
        model.DefinedByMaterialClass(materialDefinition, materialClass);
        MaterialLotState lot =
            await model.CreateMaterialLotAsync(model.Root, "Lot-1001", ct)
                .ConfigureAwait(false);
        model.DefinedByMaterialDefinition(lot, materialDefinition);
        MaterialSublotState sublot =
            await model.CreateMaterialSublotAsync(lot, "Sublot-A", ct)
                .ConfigureAwait(false);
        model.MadeUpOfMaterialSublot(lot, sublot);

        Isa95GeoSpatialLocationBinding location =
            await model.CreateGeoSpatialLocationAsync(
                model.Root,
                "PlantLocation",
                locationProvider,
                PlantSourceId,
                cancellationToken: ct).ConfigureAwait(false);
        PhysicalAssetPropertyState locationProperty = await model.AddPropertyAsync(
            vessel,
            "LocationReference",
            cancellationToken: ct).ConfigureAwait(false);
        model.LocatedIn(locationProperty, location.State);
        locationBinding = location;
    });

builder.Services.AddHostedService<DemoJobSeeder>();

using IHost host = builder.Build();
try
{
    await host.RunAsync().ConfigureAwait(false);
}
finally
{
    locationBinding?.Dispose();
}
return 0;
