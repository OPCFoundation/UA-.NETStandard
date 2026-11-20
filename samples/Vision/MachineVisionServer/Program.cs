// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MachineVisionSample;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Perception;
using Opc.Ua.Vision.Server;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.EnableOpcUaRedaction();
builder.Logging.AddConsole();
string endpoint = builder.Configuration["endpoint"] ?? "opc.tcp://localhost:62550/MachineVision";
string? insecureOption = builder.Configuration["insecure"];
bool insecure = false;
if (insecureOption != null && !bool.TryParse(insecureOption, out insecure))
{
    throw new ArgumentException("--insecure requires true or false.", nameof(args));
}
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(services => new DemonstrationVisionProvider(services.GetRequiredService<TimeProvider>()));
builder.Services.AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "MachineVisionServer";
        options.ApplicationUri = "urn:localhost:OPCFoundation:MachineVisionServer";
        options.PkiRoot = builder.Configuration["pki"] ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OPCFoundation", "MachineVisionServer", "pki");
        options.AutoAcceptUntrustedCertificates = insecure;
        options.EndpointUrls.Add(endpoint);
    })
    .AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks)
    .AddVisionToMachinery()
    .ConfigureVisionFor<MachineryNodeManager>(async (context, ct) =>
    {
        DemonstrationVisionProvider provider = context.GetRequiredService<DemonstrationVisionProvider>();
        MachineVisionSystemHandle system = await context.AddMachineVisionSystemAsync(
            "InspectionControl", provider,
            new MachineVisionSystemOptions { InitiallyReady = true, InitiallySimulated = true },
            cancellationToken: ct).ConfigureAwait(false);
        context.Nodes.AddImageSensor("Camera", sensor => sensor
            .WithSensorId("synthetic-camera")
            .WithRealityKind(VisionRealityKindEnum.Simulated)
            .WithResolution(640, 480)
            .WithPixelFormat("Mono8"));
        var sensors = new List<BaseInstanceState>();
        context.Root.Sensors!.GetChildren(context.Context, sensors);
        ImageSensorState camera = sensors.OfType<ImageSensorState>().Single();
        context.Nodes.AddPipeline("Inspection", pipeline => pipeline
            .WithPipelineId("synthetic-inspection")
            .WithSensor(camera.NodeId)
            .UseInferenceProvider(provider));
        var pipelines = new List<BaseInstanceState>();
        context.Root.Pipelines!.GetChildren(context.Context, pipelines);
        InferencePipelineState draft = pipelines.OfType<InferencePipelineState>().Single();
        provider.Attach(context, system, draft);

        MachineVisionAssetBuilder asset = context.AddMachineVisionAsset("InspectionAsset")
            .WithIdentification(new MachineryIdentificationData
            {
                Manufacturer = new LocalizedText("OPC Foundation"),
                Model = new LocalizedText("Deterministic inspection demonstrator"),
                SerialNumber = "DEMO-001",
                ProductInstanceUri = "urn:opcfoundation:machinevision:demo"
            })
            .WithComponents()
            .RepresentsSameSystem(system.State);
        MachineVisionNodeBuilder cameraAsset = asset.AddItem(MachineVisionItemKind.ImageSensor, "Camera");
        cameraAsset.WithIdentification(new MachineryIdentificationData
        {
            Manufacturer = new LocalizedText("OPC Foundation"),
            SerialNumber = "SYNTHETIC-001"
        })
            .WithHealth(health => health.Set("Temperature", Variant.From(22.5)));
        cameraAsset.State.AddReference(Opc.Ua.ReferenceTypeIds.RepresentsSameEntityAs, false, camera.NodeId);
        camera.AddReference(Opc.Ua.ReferenceTypeIds.RepresentsSameEntityAs, true, cameraAsset.State.NodeId);
        await asset.BuildAsync(ct).ConfigureAwait(false);
    });

using IHost host = builder.Build();
string? runForSeconds = builder.Configuration["runForSeconds"];
if (runForSeconds != null)
{
    if (!int.TryParse(runForSeconds, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) || seconds <= 0)
    {
        throw new ArgumentException("--runForSeconds requires a positive integer.", nameof(args));
    }
    await host.StartAsync().ConfigureAwait(false);
    await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
    await host.StopAsync().ConfigureAwait(false);
}
else
{
    await host.RunAsync().ConfigureAwait(false);
}
