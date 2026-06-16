# OPC UA Console Reference Subscriber

A self-contained .NET 10 console application that subscribes to an OPC
UA Part 14 PubSub DataSet over UDP/UADP or MQTT (UADP or JSON) using
the fluent + DI hosting surface introduced in v2.0 of the .NET
Standard stack. Pairs with `Applications/ConsoleReferencePublisher`.

## Quick start (UDP, default)

```pwsh
dotnet run -- --profile udp-uadp
```

The subscriber binds the loopback multicast group
`opc.udp://239.0.0.1:4840`, filters for `PublisherId=1` /
`WriterGroupId=100` / `DataSetWriterId=1`, and prints every decoded
DataSetMessage to the console.

## Profiles

| `--profile`  | Transport | Encoding |
|--------------|-----------|----------|
| `udp-uadp`   | UDP datagram (Part 14 §7.3.2) | UADP binary (Part 14 §5.3) |
| `mqtt-uadp`  | MQTT broker (Part 14 §7.3.4)  | UADP binary (Part 14 §5.3) |
| `mqtt-json`  | MQTT broker (Part 14 §7.3.4)  | JSON       (Part 14 §5.4) |

The MQTT profiles assume a broker reachable at `mqtt://localhost:1883`
unless overridden via `--endpoint`.

## CLI flags

| Flag                         | Default                | Description |
|------------------------------|------------------------|-------------|
| `--profile`                  | `udp-uadp`             | Wire profile. |
| `--config-file`              | _(unset)_              | Loads a Part 14 XML PubSub configuration instead of building one in-code. |
| `--publisher-id-filter`      | `1`                    | PublisherId filter (Part 14 §6.2.9). |
| `--writer-group-id-filter`   | `100`                  | WriterGroupId filter. |
| `--data-set-writer-id-filter`| `1`                    | DataSetWriterId filter. |
| `--endpoint`                 | profile-specific       | Transport endpoint URL. |

## Configuration via XML

```pwsh
dotnet run -- --profile udp-uadp --config-file Configuration\PubSubConfig.xml
```

When `--config-file` is supplied the subscriber loads the XML through
`XmlPubSubConfigurationStore` (Part 14 §9.1.6) and skips the in-code
builder; the same in-process `ConsoleLoggingSink` is still wired to
the DataSetReader named `Reader 1`.

## NativeAOT publish

```pwsh
dotnet publish -c Release -r win-x64
```

The csproj sets `<PublishAot>true</PublishAot>` on `net10.0` and
references only the trim-clean PubSub libraries plus
`Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Logging.Console`
and `System.CommandLine`. The published executable lives under
`bin/Release/net10.0/<rid>/publish/ConsoleReferenceSubscriber.exe` and
boots a complete PubSub subscriber with no JIT and no
reflection-driven configuration binding.

## Fluent builder walkthrough

`Program.cs` shows the canonical subscriber wiring shape:

```csharp
builder.Services.AddSingleton<IPubSubApplication>(sp =>
{
    ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
    var sink = new ConsoleLoggingSink(loggerFactory.CreateLogger<ConsoleLoggingSink>());

    PubSubApplicationBuilder pb = new PubSubApplicationBuilder(telemetry)
        .WithApplicationId("urn:opcfoundation:ConsoleReferenceSubscriber")
        .UseAllStandardEncoders()                       // Part 14 §5.3 / §5.4
        .AddSubscribedDataSetSink("Reader 1", sink);    // Part 14 §6.2.9

    foreach (IPubSubTransportFactory factory
        in sp.GetServices<IPubSubTransportFactory>())
    {
        pb.AddTransportFactory(factory);                // Part 14 §7.3
    }
    return pb
        .UseConfiguration(SubscriberConfigurationBuilder.Build(...))
        .Build();                                       // Part 14 §9.1.2
});

builder.Services.AddOpcUa()
    .AddPubSubSubscriber()                              // hosted-service plumbing
    .AddUdpTransport()                                  // Part 14 §7.3.2
    .AddMqttTransport();                                // Part 14 §7.3.4
```

The runtime walks the `IPubSubApplication`'s ReaderGroup → DataSetReader
hierarchy and dispatches every decoded `DataSetMessage` through the
sink keyed by reader name. To project the values into an OPC UA Server
address space, swap `ConsoleLoggingSink` for `TargetVariablesSink`; to
mirror them in memory, use `MirroredVariablesSink`.
