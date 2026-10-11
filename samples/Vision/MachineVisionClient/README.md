# Machine Vision client

Discovers the [sample server](../MachineVisionServer), connects using
Basic256Sha256 SignAndEncrypt, uploads and prepares a recipe, starts a job,
waits for Ready, retrieves results, and reads the Part 2 asset.
When a draft pipeline is present it also runs that path and resolves its result
through the released result-management API.

From the repository root, after starting the server:

```powershell
dotnet run --project samples\Vision\MachineVisionClient\MachineVisionClient.csproj -f net10.0 -- --insecure true
```

Options are `--endpoint URL`, `--pki PATH`, and `--insecure true`.
The default endpoint is `opc.tcp://localhost:62550/MachineVision`.
The complete workflow has a 60-second cancellation bound.

`--insecure true` opts into untrusted-certificate acceptance for this local demonstration;
it does not switch off transport encryption. For production, omit it and provision
application trust through certificate stores.

See [Machine Vision](../../../docs/MachineVision.md#typed-clients) for direct and DI
client construction, the generated sub-clients, streaming, and file lifetimes.
