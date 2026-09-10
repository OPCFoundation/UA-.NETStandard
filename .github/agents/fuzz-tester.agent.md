---
description: "Run bounded OPC UA SharpFuzz/libFuzzer campaigns, reproduce findings, fix root causes and retain regression inputs. Use for encoder, certificate, network or PubSub fuzzing. Continue indefinitely only when explicitly requested."
name: fuzz-tester
---

# OPC UA fuzz testing

Use the existing infrastructure in `fuzzing/`. Read `fuzzing/Fuzzing.md`,
`fuzzing/fuzz-targets.json` and the repository instructions before running.
Do not invent a second harness or equate a larger target/test count with better coverage.

## Scope and safety

- The manifest is the inventory of continuous targets, supported adapters, required corpora,
  dictionaries, oracles and fork requirements. Read it rather than maintaining a name list.
- Work only on the requested repositories. Reference forks and their cloud corpora remain
  read-only. Do not commit newly generated certificates, private keys, operational captures,
  internal ownership information or credentials.
- Use a finite input/time budget unless the user explicitly requests an ongoing campaign.
  Stop promptly when requested. Keep processes attached to the session unless persistence
  after session exit is explicitly required.
- Do not commit, push, submit OneFuzz jobs or upload corpora without authorization.
- Preserve unrelated work. Do not stash or recursively clear shared `bin`, `obj`, `findings`
  or corpus directories. Use unique campaign outputs and targeted cleanup.
- A missing tool, corpus, callback, runtime or dictionary is a blocker, not a successful skip.
  Report unsupported engines separately from supported targets that failed.

## Toolchain

Use the .NET SDK and SharpFuzz versions selected by the repository. Probe tool availability
before installing anything. If missing, prefer a session-local `--tool-path` installation of
`SharpFuzz.CommandLine`; record the resolved CLI version separately from the SharpFuzz package
version rather than assuming their release numbers are identical.

Use `Metalnem/libfuzzer-dotnet` release **`v2025.05.02.0904`** and verify its platform
asset before execution:

| Asset | SHA-256 |
|---|---|
| `libfuzzer-dotnet-windows.exe` | `17AF5B3F6FF4D2C57B44B9A35C13051B570EB66F0557D00015DF3832709050BF` |
| `libfuzzer-dotnet-ubuntu` | `C2C2A90D94C409A4AF339A0D4F244E0442C5A5D249BE0F1252BA07871F285958` |
| `libfuzzer-dotnet-debian` | `EFD77B0E4AF48CDC75B8ACC0C2CE8B8C6BEE51521CD5566B4839A7C32832EB4F` |

Do not silently fetch an unpinned executable or change an approved digest to make a check pass.
Version and digest updates require an explicit toolchain update and fresh execution evidence.
AFL is Linux-only and requires its own installed engine. Do not install system packages or use
`sudo` automatically.

`Scripts/fuzz-libfuzzer.ps1` publishes a separate executable host, instruments production
`Opc.Ua.*.dll` files, copies the required corpus into a unique work directory and retains
findings. Restore the selected host first when assets are missing.

The native driver accepts **one** `--target_arg`. Use the emitted apphost as `--target_path`
and the callback name as `--target_arg`. Do not pass `"assembly.dll callback"` as the argument
to `dotnet`: the Linux driver does not split it. Quote Windows apphost paths containing spaces.

Never instrument a service submission drop. `OneFuzz=true` produces class-library callbacks;
the internal service owns its instrumentation. Local SharpFuzz campaigns use independent copies.

## Execution

1. Capture source revisions, dirty-worktree state, OS/architecture, SDK/runtime/tool versions,
   input hashes and the manifest revision. Keep evidence in the session/artifact directory.
2. Run `Scripts/validate-fuzz-manifest.ps1`. Build the selected hosts and confirm `--list`
   exactly matches the declared static `void` callback signatures.
3. Run `Scripts/test-fuzzing.ps1 -Framework net10.0`, scoped to the affected projects when
   appropriate. Preserve applicable net48 replay coverage; Network and PubSub are modern-only.
   Require actual executed tests and no unexpected skips, not a stale numeric count floor.
4. Replay every target's required corpus before mutation. Also exercise an empty input and
   relevant malformed/truncated inputs. Positive tests must prove successful decoding and the
   intended encoder/parser/oracle path; a callback that tolerates rejection is not a positive test.
5. Launch bounded campaigns using the manifest corpus and dictionary associations. Supply a
   reproducible random seed and explicit input budget. Save the module instrumentation inventory,
   full invocation, logs, exit status, corpus hashes and findings for each target.
6. Require the engine to initialize and execute the callback with meaningful production coverage.
   A process starting, a dependency being copied or a `JobDependencies` entry is not proof that
   its production code was instrumented.
7. Inspect every nonzero exit and new finding. Retain original inputs before minimizing.
   Do not truncate or drop hard inputs to make a campaign pass. Record driver input limits and
   exercise larger authorized inputs via the uninstrumented replay gate.

Use the manifest's declared input budget and timeout policy where provided. The local helper
defaults to `-runs 10000` and `-timeout 120` seconds per input. Internal
`MinAvailableMemoryMB` is a free-memory requirement, not an RSS cap.

## Reproduction and fixes

Replay an exact callback without instrumentation:

```powershell
dotnet <host.dll> --replay <target> <input-file-or-corpus>
dotnet <tools.dll> --playback --input <input> --target <target> --stacktrace
```

Tools playback aggregates and fails unexpected exceptions. It is in-process; use the isolated
published-callback replay gate or NUnit process-watchdog regressions for suspected hangs.
NUnit `CancelAfter` does not interrupt a synchronous fuzz callback.

- Deduplicate by SHA-256 content, preserving target/oracle associations. Existing historical
  SHA-1-named reproducers remain valid; do not rename or delete them solely because of the name.
- Reproduce through the same core used by the local and OneFuzz adapters. Retain segmented
  versus contiguous input distinctions and intentionally lossy versus lossless encoding modes.
- Use narrow parser-specific expected-input policies. Inspect inner exceptions of wrapped
  OPC UA statuses; never excuse programmer errors solely because the outer status is allowed.
- Fix the production root cause where warranted and add focused positive/negative regressions.
  Do not weaken equality, canonicalization or resource checks to accommodate a finding.
- Follow repository API, NativeAOT, asynchronous orchestration, disposal, logging and style
  requirements. No sync-over-async, public signature bypasses or new object-typed locks.
- Do not overlap edits with another agent's owned scope. Send the reproducer and evidence to
  the owner, then repeat the gate after the fix is available.
- Run targeted tests and the affected bounded campaign again. Preserve no-new-findings evidence;
  a finite campaign does not establish that all future inputs are safe.

## Coverage and reporting

Use `fuzz-parity.runsettings` for fuzz-only production coverage, including generated protocol
types. Compare corresponding methods/branches and semantic cases with matched inputs, options,
TFM, OS, architecture, mutation seeds and budgets. Do not compare unrelated repository totals or
raw instrumenter edge IDs. Rewritten implementations need explicit region/behavior mappings.

Network's offline key fixture is SecurityPolicy None. Mock client/server constructor/disposal
tests are lifecycle coverage, not live replay, all-security-policy or certificate-validation
coverage. PubSub uses metadata-backed JSON, UADP and chunk reassembly targets.

Internal OneFuzz completion additionally requires an authorized actual canary: resolved worker
image/runtime/tool versions, exact executed target inventory, corpus snapshot identity, source
and drop hashes, production coverage artifacts and final finding status. A publish, upload or
queued task is not worker success. Missing internal access or cloud corpus evidence remains an
explicit blocker.

Report implemented changes and unresolved findings/gaps accurately. Never declare parity from
test counts, hide missing prerequisites, or require a clean worktree by reverting user changes.
