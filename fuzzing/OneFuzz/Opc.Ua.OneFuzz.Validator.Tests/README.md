# OneFuzz validator contract tests

These NUnit tests exercise the **console CLI**, not the validator's internal
classes. They never load callback assemblies into the test host, submit jobs,
upload drops, instrument assemblies, or use the shared fuzz harness.

## Run with isolated SDK outputs

From the repository root, with the .NET 10 SDK and runtime installed:

```powershell
$artifacts = Join-Path ([IO.Path]::GetTempPath()) ("onefuzz-contract-sdk-" + [Guid]::NewGuid().ToString("N"))
dotnet test fuzzing\OneFuzz\Opc.Ua.OneFuzz.Validator.Tests\Opc.Ua.OneFuzz.Validator.Tests.csproj -c Release -p:CustomTestTarget=net10.0 --artifacts-path $artifacts
```

Always supply a unique absolute `--artifacts-path`, including for a build or
restore of this project. It propagates to both project references. Do not use a
single shared `BaseIntermediateOutputPath`.

The one-time fixture also passes a separate absolute `--artifacts-path` to every
`dotnet publish` invocation. It publishes:

1. The BCL-only `Opc.Ua.OneFuzz.TestTarget` project with
   `ContractDependencyOnly=true`, producing a real companion library.
2. The same project normally, with `ContractDependencyPath` pointing at that
   companion DLL. This produces a managed net10 callback library, portable PDBs,
   `.deps.json`, `.runtimeconfig.json`, and the actual companion dependency.
3. The validator console application.

The original dependency publish directory and SDK build outputs are then removed.
Every test copies the published callback closure into a new relocated drop with
spaces in its name. Neither a repository working directory nor the original
reference's `HintPath` can supply missing dependencies. Temporary drops, receipts,
and published fixture files are deleted at fixture teardown.

The fixture project's ordinary solution-build mode compiles its dependency helper
directly. It requires no pre-generated binaries. The companion-library publish is
a mode of the same project, **not a third project**.

## Contracts

| Test file | Contracts |
|---|---|
| `ValidatorContractTests.cs` | Exact discovery, DLL-name normalization, published closure hashes, optional configuration, exact per-target paths/hashes/counts, zero skips, callback-produced markers and unchanged drop |
| `ValidatorContractTests.Manifest.cs` | Missing/extra callbacks, wrong class, malformed/duplicate targets and JSON, area selection, explicit manifest/config paths, new results directory and watchdog argument guards |
| `ValidatorContractTests.Paths.cs` | Every required corpus bucket, missing/empty corpus, dictionaries, casing, traversal/globs, overlapping paths, equal bytes at distinct paths and direct/nested links |
| `ValidatorContractTests.Closure.cs` | Missing DLL/dependency/deps/runtimeconfig/PDB, incorrect or corrupt portable PDBs, net10 relocatable runtime settings and forbidden SharpFuzz metadata |
| `ValidatorContractTests.Configuration.cs` | ConfigVersion3, exactly four `libfuzzerDotNet` fields, exact callback mapping, Skip/baseline policy, ownership fields, one job, container boundaries, explicit complete dependencies and dictionary-token union |
| `ValidatorContractTests.Profile.cs` | Explicit ownership-profile generation, single/merged dictionaries, exact target coverage, unresolved ownership failures, no overwrites, transactional dictionary rollback, and replay through generated configurations |
| `ValidatorContractTests.Replay.cs` | Callback exception, zero exit without a receipt, incorrect receipt identity/count/path/hash, and hard watchdog termination of a noncooperative synchronous callback |

All ownership metadata is synthetic (`contract-tests.invalid`, synthetic project
names, fixture-only numeric IDs). It must not be copied into a service config.

## Negative controls are NOT service targets

`Opc.Ua.OneFuzz.TestTarget.ContractCallbacks.NegativeControl` is solely a test
fixture. Reserved input bytes deliberately throw, call `Environment.Exit(0)`,
write incorrect completion receipts, or execute an endless synchronous CPU loop.
**Never add this fixture assembly to a service manifest or upload it to OneFuzz.**
The NUnit `NegativeControl` category is descriptive; these tests are not ignored
or `[Explicit]` and run with the rest of the suite.

Every child wait is asynchronous. An outer safety timeout kills only the exact
process object (and its descendants). Watchdog tests distinguish this safety
timeout from the validator's own watchdog, check that the callback started in a
different process, and verify that the recorded PID/start-time identity is gone.
A surviving negative-control process is killed by its exact handle and fails the
test. No thread abort, sleeps, synchronous task waits, network calls, or ports are
used.

On Windows the nonparallel fixture temporarily disables Windows Error Reporting
for intentional child failures, restoring the test host's original error mode
afterwards. Directory-link tests use privilege-free NTFS junctions; other
platforms use directory symlinks.

## Solution registration

The owner of the parent solution should register:

- `fuzzing/OneFuzz/Opc.Ua.OneFuzz.Validator.Tests/Opc.Ua.OneFuzz.Validator.Tests.csproj`
- `fuzzing/OneFuzz/Opc.Ua.OneFuzz.TestTarget/Opc.Ua.OneFuzz.TestTarget.csproj`

This change intentionally does not edit the root solution, parent manifest, CI,
shared properties, validator source, or validator project.
