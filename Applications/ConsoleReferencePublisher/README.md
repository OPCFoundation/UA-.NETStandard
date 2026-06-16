# OPC UA Console Reference Publisher

A self-contained .NET 10 console application that publishes an OPC UA
Part 14 PubSub DataSet over UDP/UADP or MQTT (UADP or JSON) using the
fluent + DI hosting surface introduced in v2.0 of the .NET Standard
stack.

## Quick start (UDP, default)

```pwsh
dotnet run -- --profile udp-uadp
```

Out of the box, the publisher emits a `Simple` DataSet (`BoolToggle`,
`Int32`, `DateTime`) once per second to `opc.udp://239.0.0.1:4840`.
A loopback subscriber (see `Applications/ConsoleReferenceSubscriber`)
or any standard OPC UA PubSub UDP/UADP consumer can ingest it.

## Profiles

| `--profile`  | Transport | Encoding |
|--------------|-----------|----------|
| `udp-uadp`   | UDP datagram (Part 14 §7.3.2) | UADP binary (Part 14 §5.3) |
| `mqtt-uadp`  | MQTT broker (Part 14 §7.3.4)  | UADP binary (Part 14 §5.3) |
| `mqtt-json`  | MQTT broker (Part 14 §7.3.4)  | JSON       (Part 14 §5.4) |

The MQTT profiles assume a broker reachable at `mqtt://localhost:1883`
unless overridden via `--endpoint`.

## CLI flags

| Flag                       | Default                        | Description |
|----------------------------|--------------------------------|-------------|
| `--profile`                | `udp-uadp`                     | Wire profile. |
| `--config-file`            | _(unset)_                      | Loads a Part 14 XML PubSub configuration instead of building one in-code. Mutually exclusive with the in-code builder path. |
| `--publisher-id`           | `1`                            | `ushort` PublisherId placed in every NetworkMessage header (Part 14 §6.2.7). |
| `--writer-group-id`        | `100`                          | WriterGroupId for the single WriterGroup. |
| `--data-set-writer-id`     | `1`                            | DataSetWriterId for the single DataSetWriter. |
| `--endpoint`               | profile-specific               | Transport endpoint URL. |
| `--interval`               | `1000`                         | Publishing interval in milliseconds. |

## Configuration via XML

```pwsh
dotnet run -- --profile udp-uadp --config-file Configuration\PubSubConfig.xml
```

When `--config-file` is supplied the publisher loads the XML through
`XmlPubSubConfigurationStore` (Part 14 §9.1.6) and skips the in-code
builder; the same in-process `SampleDataSetSource` still feeds every
PublishedDataSet named `Simple`.

## NativeAOT publish

```pwsh
dotnet publish -c Release -r win-x64
```

The csproj sets `<PublishAot>true</PublishAot>` on `net10.0` and
references only the trim-clean PubSub libraries plus
`Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Logging.Console`
and `System.CommandLine`. The published executable lives under
`bin/Release/net10.0/<rid>/publish/ConsoleReferencePublisher.exe` and
boots a complete PubSub publisher with no JIT and no reflection-driven
configuration binding.

## Fluent builder walkthrough

`Program.cs` shows the canonical wiring shape:

```csharp
builder.Services.AddSingleton<IPubSubApplication>(sp =>
{
    ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
    PubSubApplicationBuilder pb = new PubSubApplicationBuilder(telemetry)
        .WithApplicationId("urn:opcfoundation:ConsoleReferencePublisher")
        .UseAllStandardEncoders()                       // Part 14 §5.3 / §5.4
        .AddDataSetSource("Simple", sampleSource);      // Part 14 §6.2.3
    foreach (IPubSubTransportFactory factory
        in sp.GetServices<IPubSubTransportFactory>())
    {
        pb.AddTransportFactory(factory);                // Part 14 §7.3
    }
    return pb
        .UseConfiguration(PublisherConfigurationBuilder.Build(...))
        .Build();                                       // Part 14 §9.1.2
});

builder.Services.AddOpcUa()
    .AddPubSubPublisher()                               // hosted-service plumbing
    .AddUdpTransport()                                  // Part 14 §7.3.2
    .AddMqttTransport();                                // Part 14 §7.3.4
```

* `PubSubApplicationBuilder` is the manual non-DI fluent surface
  (mirrors `ManagedSessionBuilder` for Opc.Ua.Client).
* `AddPubSubPublisher` registers the supporting services (telemetry,
  scheduler, metadata registry, security policies, hosted service);
  because the sample pre-registers its own `IPubSubApplication`, the
  DI extension's `TryAddSingleton<IPubSubApplication>` is a no-op and
  the hosted service drives the sample-built application.
* `AddUdpTransport` and `AddMqttTransport` register the per-transport
  `IPubSubTransportFactory` instances; the fluent builder pulls them
  out of DI and feeds them to the application.

To swap the demo data source for a real one, replace
`SampleDataSetSource` with any `IPublishedDataSetSource` (for example,
one backed by a `Session` `Read`).
