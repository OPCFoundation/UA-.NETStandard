# UaLens — OPC UA desktop client

UaLens is a multi-tab Avalonia desktop client for OPC UA, contributed by
the OPC Foundation as a reference UI on top of the
`OPCFoundation.NetStandard.Opc.Ua.Client` stack. Tabs cover live
subscriptions, GDS push / pull / discovery, event monitoring, history
read / update, file-system browsing, performance benchmarks, and
certificate management.

## Install

```bash
dotnet tool install -g OPCFoundation.NetStandard.Opc.Ua.Lens
ualens
```

(Requires .NET 10 SDK or runtime.)

## Highlights

- Multi-tab workbench: every workflow (Subscription, GDS Push,
  GDS Management, GDS Discovery, Event View, Historian, Performance,
  File System) is a separate tab.
- Real OPC UA stack — UaLens is a thin Avalonia front-end over the
  reference `OPCFoundation.NetStandard.Opc.Ua.Client` library; the same
  protocol logic that ships in the SDK.
- Find-by-path, locale picker, address-space view modes
  (Objects / Types / DataTypes / ReferenceTypes / Views), and saved
  endpoint favourites mirror the WinForms reference client.

## Reporting bugs

https://github.com/OPCFoundation/UA-.NETStandard/issues

## License

MIT (see `LICENSE.txt` in the repository root).