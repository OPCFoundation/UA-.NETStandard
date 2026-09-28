# MachineryClient

A console client for any OPC 40001 server. It starts at the `Machines` folder,
so it needs no NodeIds configured and works against a server it has never seen.

```sh
dotnet run --project samples/Machinery/MachineryClient -- --insecure
```

| Option | Meaning |
| --- | --- |
| `--url <endpoint>` | The endpoint to connect to (default `opc.tcp://localhost:62546/MachineryServer`) |
| `--insecure` | Accept any server certificate. Demo only. |
| `--seconds <n>` | How long to stream state transitions (default 12; `0` skips) |
| `--no-download` | Skip the OPC 40001-101 result download |

## What it does

1. Enumerates the machines below the `Machines` folder.
2. Reads each machine's OPC 40001-1 identification and its components.
3. Reads `MachineryItemState` and `MachineryOperationMode`.
4. Asks `ResultManagement.GetLatestResult` for the newest result, downloads its
   payload through the OPC 40001-101 transfer path, and releases the result
   handle it was given.
5. Streams `MachineryItemState` transitions for a while.

Every step degrades rather than failing when the server publishes less: a
machine without a `Monitoring` add-in simply reports no state.
