# Fuzz crash corpus

The corpus checked in under `fuzzing/*.Fuzz.Corpus` and `fuzzing/*/Assets/Repo` runs on every pull
request. The full **crash corpus** holds about 22k crash, timeout and slow inputs that earlier
fuzzing campaigns found. It is too large to replay on every pull request, so it lives on the orphan
branch **`fuzz-corpus`** of this repository and the nightly workflow replays it.

## History: the Azure DevOps secure file

Until the move to GitHub Actions, the corpus was the Azure DevOps secure file
`FuzzingArtifacts.zip` (**Pipelines → Library → Secure files**).
[`.azurepipelines/test.yml`](../.azurepipelines/test.yml) downloaded it with `DownloadSecureFile@1`,
but only for the `Opc.Ua.Encoders.Fuzz.Tests` matrix entry. It extracted the file over
`fuzzing/Opc.Ua.Encoders.Fuzz.Tests/` and re-published it as the `drop` artifact whenever the job
failed.

It was treated as private, but it was not private in practice:

- the `drop` artifacts could be downloaded anonymously;
- `FuzzCrashAssets` printed a base64 `REPRODUCER` line into anonymously readable build logs for
  every input that still threw.

All inputs date from April to June 2024, and current `master` replays every one of them without a
test failure. The archive was therefore published unchanged as the first commit of `fuzz-corpus`.
Its SHA-256 is `85198e46518217241de5dc1eefcb5a4e40b18e62df1a2da1f333c3a39fe1d61b`, and it was
recovered from the `drop` artifact of Azure build 18397.

The `master378` and 1.x release pipelines keep their own copy of `test.yml` and still use the
secure file, so do not delete it from the Azure Library.

## Branch layout

`fuzz-corpus` shares no history with `master` and is never merged into it. Each top-level folder
mirrors a `fuzzing/<project>` folder and is copied over it unchanged:

```
fuzz-corpus
├── .gitattributes                   # "* binary": inputs are raw bytes, never normalized
├── README.md
└── Opc.Ua.Encoders.Fuzz.Tests/      # overlays fuzzing/Opc.Ua.Encoders.Fuzz.Tests/
    └── Assets/
        ├── crash-<sha1>             # 22,267
        ├── slow-unit-<sha1>         # 25
        └── timeout-<sha1>           # 1
```

[`FuzzTargetTestsBase`](Common/Fuzz.Tests/FuzzTargetTestsBase.cs) enumerates `Assets/crash*.*`,
`timeout*.*` and `slow*.*` and replays each input through every fuzz target:

- A crash input must not throw any more. The one exception is an encoding-fidelity finding on an
  input outside `Assets/Repo`, which is reported but does not fail the test.
- Timeout and slow inputs must finish within the watchdog.

Other areas, such as `Opc.Ua.Network.Fuzz.Tests/Assets/…`, can be added as further top-level
folders. The nightly matrix must then also run those projects (see below).

## How CI replays it

The `crash-corpus` job in [`.github/workflows/nightly.yml`](../.github/workflows/nightly.yml) runs
on every scheduled and manual nightly run. It:

1. checks out the commit pinned in the job's `FUZZ_CORPUS_COMMIT` into `.fuzz-crash-corpus`. It is a
   plain `actions/checkout` of this repository, with no secrets or environment involved;
2. verifies the checked-out SHA, copies each top-level folder over `fuzzing/<folder>` and records
   the commit and file count in the job summary;
3. runs `Opc.Ua.Encoders.Fuzz.Tests` on every non-macOS profile through the shared
   `run-dotnet-tests` action and uploads `crash-corpus-results-*`.

The nightly summary fails if the job was skipped. The job narrows its matrix with
`-OnlyProject 'fuzzing/Opc.Ua.Encoders.Fuzz.Tests/Opc.Ua.Encoders.Fuzz.Tests.csproj'` in the
`discover` job, so widen that when the corpus gains other areas.

The pin is the point: pushing to `fuzz-corpus` changes nothing until a reviewed pull request to
`master` moves `FUZZ_CORPUS_COMMIT`.

## Replaying it locally

```powershell
git fetch origin fuzz-corpus
git worktree add ../fuzz-corpus origin/fuzz-corpus
Copy-Item ../fuzz-corpus/Opc.Ua.Encoders.Fuzz.Tests/* fuzzing/Opc.Ua.Encoders.Fuzz.Tests/ -Recurse -Force
dotnet test fuzzing/Opc.Ua.Encoders.Fuzz.Tests -c Release -p:CustomTestTarget=net10.0
git clean -fdx fuzzing/Opc.Ua.Encoders.Fuzz.Tests/Assets
```

`.gitignore` does not exclude the overlaid files, so the final `git clean` is what keeps them out
of a commit. `Assets/Repo` and `Assets/ForkMessages` are tracked and are not touched.

## Adding inputs

1. **Unfixed crashes are not added here.** An input that still crashes the stack is a potential
   vulnerability. Report it privately as described in [SECURITY.md](../SECURITY.md) and fix it
   first.
2. Once the fix has shipped, open a pull request against `fuzz-corpus` that adds the input as
   `<project>/Assets/crash-<sha1>`, `timeout-<sha1>` or `slow-<sha1>`, keeping libFuzzer's
   content-hash name.
3. After it merges, open a pull request against `master` that sets `FUZZ_CORPUS_COMMIT` in
   `nightly.yml` to the new commit. That run is the first to replay the new inputs.
4. A minimized input that should gate every pull request also belongs in
   `fuzzing/<project>/Assets/Repo/` on `master`, where it is checked strictly.

`fuzz-corpus` should be covered by a branch ruleset that requires pull requests and blocks force
pushes and deletion. Force pushes are the risk: a pinned commit that is no longer reachable from
any branch may eventually be garbage-collected, and the nightly checkout would then fail.
