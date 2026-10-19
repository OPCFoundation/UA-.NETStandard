# OPC 30010 AutoID samples

Two console applications that show the `Opc.Ua.AutoID*` libraries end to end: a
server that publishes a simulated RFID reader, and a client that discovers it,
scans, reads and writes tag memory and prints the events.

| Sample | What it does |
| --- | --- |
| [AutoIdServer](AutoIdServer/) | Serves the reader `DockDoor1` backed by `InMemoryRfidReaderProvider`: two pallets in range, a third entering and leaving every two seconds, a triggered scan every six |
| [AutoIdClient](AutoIdClient/) | Discovers the readers, prints the device, runs a synchronous scan, reads and writes tag memory, then starts a scan and prints scan and presence events |

The [AutoID guide](../../docs/AutoID.md) explains every API used here.

## Run

In two terminals:

```sh
dotnet run --project samples/AutoID/AutoIdServer -- --port 62549
dotnet run --project samples/AutoID/AutoIdClient -- --insecure --seconds 10
```

`--insecure` makes the client accept the server's self-signed certificate;
never use it in production. The client also takes `--url <endpoint>` (default
`opc.tcp://localhost:62549/AutoIdServer`) and `--seconds <n>` (how long to watch
events, `0` to skip). The server takes `--port`, `--host`, `--pki-root`,
`--autoaccept` and `--run-seconds`. The default port is in the dynamic range and
is not assigned by IANA.

The client prints something like:

```
Rfid reader DockDoor1  (ns=1;i=4146858853)

DockDoor1: Idle, Example Automation RFID portal R-100, serial R100-0001
  Dock door antenna portal at Dock 1, gate A
  antennas: 1=Left post, 2=Right post

Scan: SUCCESS, 2 tag(s)
  E28011600000200000000101  antenna 2, -48 dBm
  E28011600000200000000201  antenna 1, -51 dBm

ReadTag user memory: SUCCESS, "PALLET-0001"
WriteTag: SUCCESS; read back: "CHECKED"

ScanStart, watching scan events for 10 s ...
ScanStart: SUCCESS
  scan event at 10:15:31: 000101, 000201
  presence: 3 tag(s) in range
  scan event at 10:15:32: 000101, 000201, 000301
ScanStop: stopped
```

`AutoIdClient/Test-AutoIdSamplePair.ps1` starts the server on a free port, runs the
client and checks the output:

```powershell
./samples/AutoID/AutoIdClient/Test-AutoIdSamplePair.ps1 `
    -ServerExecutable samples/AutoID/AutoIdServer/bin/Debug/net10.0/AutoIdServer.dll `
    -ClientExecutable samples/AutoID/AutoIdClient/bin/Debug/net10.0/AutoIdClient.dll
```
