/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MinimalIsa95Server;
using Opc.Ua;
using Opc.Ua.ISA95;
using Opc.Ua.ISA95.Server.Builders;
using Opc.Ua.ISA95.Server.Providers;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.EnableOpcUaRedaction();
builder.Logging.AddConsole();

int port = int.TryParse(builder.Configuration["port"], out int configuredPort)
    ? configuredPort
    : 62545;
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
        options.AutoAcceptUntrustedCertificates = true;
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
