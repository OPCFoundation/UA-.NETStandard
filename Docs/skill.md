# Skill: `dotnet format` cleanup recipe for `UA.slnx`

A working recipe for applying the rules from `.editorconfig` across the
solution using `dotnet format` and recovering when the auto-fixer
mis-translates code.

## Repo facts

* `LangVersion = 14.0` (no C# 15 features).
* `TreatWarningsAsErrors = true` — applies to compiler (CS) warnings.
* `CodeAnalysisTreatWarningsAsErrors = false` — analyzer (CA/IDE/RCS/NUnit)
  warnings stay warnings even when an MSBuild build is run; only those
  explicitly raised to `error` severity in `.editorconfig` fail the
  build.
* `RunAnalyzersDuringBuild = false` by default — analyzers run only
  on demand via `/p:RunAnalyzersDuringBuild=true`.
* `EnableNETAnalyzers = true`, `AnalysisMode = all`,
  `AnalysisLevel = preview`.
* Roslynator analyzer packages are referenced
  (`Roslynator.Analyzers`, `Roslynator.Formatting.Analyzers`) plus the
  NUnit3 analyzers that ship with the test framework.
* `.editorconfig` is the authoritative rule source. Only rules at
  `error`, `warning`, or `suggestion` severity are active.
  `none`/`silent` are intentionally off and stay off.

## Inventory commands

Use `--verify-no-changes` to enumerate remaining diagnostics without
modifying any file:

```pwsh
dotnet format whitespace UA.slnx --verify-no-changes --severity info
dotnet format style      UA.slnx --verify-no-changes --severity info
dotnet format analyzers  UA.slnx --verify-no-changes --severity info
```

To group a build log by diagnostic ID:

```pwsh
dotnet build UA.slnx -c Debug --nologo /p:RunAnalyzersDuringBuild=true 2>&1 |
    Tee-Object -FilePath baseline.log
Select-String baseline.log -Pattern '\b(CS|CA|IDE|RCS|NUnit)[0-9]+\b' -AllMatches |
    ForEach-Object { $_.Matches.Value } | Group-Object | Sort-Object Count -Descending
```

(Each ID typically appears 2 × N times: once in the message text plus
once in the URL, for each TFM the project targets.)

## Apply commands

```pwsh
# Pure whitespace (trailing whitespace, blank-line normalisation,
# missing final newlines).
dotnet format whitespace UA.slnx

# Style — IDE-prefixed Roslyn rules. The --diagnostics flag scopes the
# fixer to those IDs; other style rules can still run as side-effects
# (e.g., 'sort using directives').
dotnet format style UA.slnx --diagnostics IDE0032 IDE0049 --severity info
dotnet format style UA.slnx --diagnostics IDE1005          --severity info

# Analyzers — CA/RCS/NUnit prefixes. Multiple IDs at once is fine, but
# if a batch breaks the build, halve the IDs and re-run.
dotnet format analyzers UA.slnx --diagnostics RCS1043 RCS1085 --severity info
dotnet format analyzers UA.slnx --diagnostics NUnit2010       --severity info
```

`--severity info` is necessary to pick up rules at `suggestion` severity
in `.editorconfig`; the default of `warn` skips them.

## Verification commands

The build verification command — every batch must end in **0 errors** —
is:

```pwsh
dotnet build UA.slnx -c Debug --nologo /p:RunAnalyzersDuringBuild=true
```

Spot tests after each library batch:

```pwsh
dotnet test Tests\Opc.Ua.Core.Tests\Opc.Ua.Core.Tests.csproj `
    -c Debug -f net10.0 --no-build --verbosity quiet
```

For test-project changes, run the **full** suite for that project on
**every** TFM the project targets. `dotnet test` without `-f` runs all
configured TFMs:

```pwsh
dotnet test Tests\Opc.Ua.Client.Tests\Opc.Ua.Client.Tests.csproj `
    -c Debug --no-build --verbosity quiet
```

Net48 parity check for Core/Server every 3 batches:

```pwsh
dotnet test Tests\Opc.Ua.Core.Tests\Opc.Ua.Core.Tests.csproj `
    -c Debug -f net48 --no-build --verbosity quiet
dotnet test Tests\Opc.Ua.Server.Tests\Opc.Ua.Server.Tests.csproj `
    -c Debug -f net48 --no-build --verbosity quiet
```

## Order of operations

```
whitespace  →  IDE  →  CA  →  RCS  →  NUnit  →  manual cleanup  →  docs
```

Why: whitespace is the largest, lowest-risk diff and stabilises files
before any structural change. IDE rules (Roslyn style) are next — they
edit on the surface but don't restructure. CA rules from the .NET
analyzers are next; they restructure code (`ConfigureAwait`,
`IDisposable` additions). RCS rules from Roslynator are aggressive
and frequently auto-rewrite control flow; run them after the other
families to minimise churn. NUnit conversion runs last because the
class assertion → `Assert.That` translation is the riskiest auto-fix
and you want a stable baseline before touching tests.

## Per-rule notes

### Auto-fixed cleanly

| ID | Notes |
| --- | --- |
| `IDE0049` | `dotnet format style` keeps language keywords (e.g. `Int32`→`int`) consistent. Bundled into the IDE0032 batch in this run; no behavioural risk. |
| `RCS0027` / `RCS0029` | Trailing-vs-leading binary operators on wrapped lines. Pure formatting. |
| `RCS0050` | Collapse double blank lines. Pure formatting. |
| `RCS1043` | Removes `partial` modifier when only one part exists. Verify no other partials before commit (search for `partial class <name>`). |
| `RCS1049` | `x == true`/`== false` simplification → `x` / `!x`. Safe for `bool`; never applied to nullable bool by Roslynator. |
| `RCS1061` | Merge nested `if`s with `&&`. Safe. |
| `RCS1085` | Backing field + expression-bodied property → auto-property `{ get; }`. Safe; the field disappears and assignments to it become assignments to the property in the same type. |
| `RCS1090` | Drops unnecessary parens around lambdas. Safe. |
| `RCS1132` | Merges fall-through `case` blocks with identical bodies. Safe. |
| `RCS1140` / `RCS1142` | Adds `<exception>` / `<typeparam>` placeholders to xml-doc comments. The placeholders are empty (`<typeparam name="T"></typeparam>`); fill them in later. |
| `RCS1186` | Inlines a multi-line auto-property declaration to a single line. |
| `RCS1249` | Removes the null-forgiving `!` suffix when the compiler can already prove non-null. Watch for places where `!` was load-bearing for an external nullable contract. |
| `RCS1252` | Removes `try`/`finally` whose `finally` block is empty. Safe. |
| `NUnit2009` | `is 0 or 1` pattern → `Is.EqualTo(0).Or.EqualTo(1)`. |
| `NUnit2010` | `Assert.That(x.Equals(y), Is.True)` → `Assert.That(x, Is.EqualTo(y))`. Verify the type has a real `Equals` override before relying on this. |

### Required narrow suppression

Tracked as a running ledger. Each suppression uses the smallest
enclosing scope (single statement or member declaration) and a one-line
comment with the reason.

| ID | File:Line | Reason | Resolution |
| --- | --- | --- | --- |
| `RCS1047` | `Stack/Opc.Ua.Core/Stack/Server/ServerBase.cs:760` | `OnCertificateUpdateAsync` is `protected virtual void` — renaming would be a binary breaking change for derived classes. | `#pragma warning disable RCS1047` around the declaration. |
| `NUnit2010` | `Tests/Opc.Ua.Security.Certificates.Tests/CertificateWrapperTests.cs:329` | Test is `EqualsSameReferenceReturnsTrue` — `cert.Equals(cert)` is the assertion under test. Auto-rewrite to `Is.EqualTo(self)` immediately trips `NUnit2009` (actual == expected). | `#pragma warning disable NUnit2010` around the assertion. |

### Manual edits (not auto-fixable)

| ID | File:Line | Change |
| --- | --- | --- |
| `RCS1174` | `Stack/Opc.Ua.Core/Security/Certificates/CertificateManager/CertificateManager.cs:493` | Convert `async`-with-only-tail-await to a non-async method that returns the inner Task; argument validation still throws synchronously. |
| `NUnit2023` | `Tests/Opc.Ua.Core.Tests/Stack/Types/X509IdentityTokenHandlerTests.cs:61` | `Assert.That(value, Is.Not.Null)` on a `struct` is tautological; remove the assertion. |
| (alignment) | `Stack/Opc.Ua.Core/Security/Certificates/CertificateManager/CertificateManager.cs:113` | Roslynator's `RCS0033`/continuation-indent fixer shifted the `[`, the items, and the `]` of a collection-expression initializer by different amounts. Re-align manually so the `]` matches the column of the `[`. |

### Deferred — fix manually in follow-up work

The remaining 1858 warnings under `RunAnalyzersDuringBuild=true` are
all `CA*` analyzer rules whose fixers either don't exist, can't be
invoked through `dotnet format`, or would produce semantically
incorrect code. They are real engineering work, not formatting.

| ID | Approx. instances | Why deferred |
| --- | --- | --- |
| `CA2000` | ~470 | "Dispose objects before losing scope" — needs per-call-site review. The disposable is sometimes ownership-transferred to the return value or to a field; auto-fixing to a `using` would prematurely dispose. |
| `CA2007` | ~130 | "Do not directly await a Task" — would add `.ConfigureAwait(false)` everywhere. The team's async policy is documented in `Docs/`; sweep should be done as a single dedicated PR, not folded into a formatting commit. |
| `CA1859` | ~55 | "Use concrete types when possible" — affects local variable types; mechanical but signature-affecting. |
| `CA2016` | ~22 | "Forward CancellationToken" — safe in most cases but each site needs to confirm the inner call has a `CancellationToken` overload. |
| `CA1001` | ~17 | "Types that own disposable fields should be disposable" — adding `IDisposable` to a public type is binary-breaking. |
| `CA1508` | ~13 | "Avoid dead conditional code" — usually a real bug or a defensive null-check kept on purpose; needs manual triage. |
| `CA2213` | ~12 | "Disposable fields should be disposed" — adds work to `Dispose`; needs review of the `Dispose` pattern. |
| `CA2263` | ~7 | Generic-API preference. |
| `CA2025` | ~4 | "Avoid passing IDisposable" — refactor. |
| `CA1861` | ~4 | "Avoid constant arrays as arguments" — refactor to `ReadOnlySpan<T>`. |
| `CA1816` / `CA1823` / `CA1868` / `CA1850` / `CA5394` | each ≤2 | One-off manual fixes. |

These should be tackled in a dedicated cleanup PR, one rule family at
a time, with full test runs between each.

## Tips learned the hard way

1. **The IDE0032 fixer crashes mid-run.** `Failed to apply code fix
   CSharpUseAutoPropertyCodeFixProvider for IDE0032: SyntaxTree is not
   part of the compilation`. The pass exits cleanly but produces no
   IDE0032 changes; only the `dotnet_sort_system_directives_first`
   side-effect lands. Treat IDE0032 as "manual" until the fixer is
   fixed upstream.

2. **`dotnet format` re-runs source generators per project.** Expect
   ~5–15 minutes per command on this solution. Don't kill it early —
   it's not stuck.

3. **`/p:RunAnalyzersDuringBuild=true` triples build time.** Plan
   ~15 minutes per verification build. Combine independent batches
   only when their files don't overlap; otherwise verify after each.

4. **Warning counts vary between runs.** MSBuild incremental cache
   means a build only re-analyses projects whose inputs changed. The
   warning total reported by `Build succeeded` reflects only the
   projects rebuilt this round, not the whole solution. Don't treat
   the number as a reliable progress metric — only the per-ID
   inventory from a clean baseline is meaningful.

5. **NUnit conversions are the riskiest auto-fix.** Always run the
   full test suite of every modified test project on every TFM after
   an NUnit batch. The `Equals(self)` reflexive case (`NUnit2009`
   raised by an `NUnit2010` fix) is the canonical example.

6. **Phase 0 is mandatory.** The first build under
   `/p:RunAnalyzersDuringBuild=true` may have errors (rules at
   `error` severity in `.editorconfig`). Fix them or suppress them
   narrowly *before* any auto-format pass — otherwise every
   subsequent batch fails the verification build for reasons
   unrelated to the batch.

7. **Roslynator continuation-indent fixes can leave bracket
   misalignment.** When a collection-expression initializer is
   reflowed, the `[` and items can shift by 4 columns while the `]`
   shifts by 8. Eyeball every diff that touches an initializer.

8. **Keep `baseline.log` and `format-*-inventory.log` out of the
   commit.** Add them to `.git/info/exclude` (a local-only ignore).
   Don't add them to `.gitignore`; they're working notes, not repo
   artefacts.

9. **`dotnet format` does not honour file-level pragmas as
   suppressions.** A `#pragma warning disable RCS1047` at the top of
   a file does NOT stop the format command from trying to apply
   the fix. The fixer inspects the AST, not the editorconfig+pragma
   resolved severity. Use narrow `disable`/`restore` pairs around
   single declarations.

## Reference

* `.editorconfig` (authoritative).
* `Directory.Build.props`, `Directory.Packages.props` (analyzer
  references and analysis level).
* Roslynator rule catalog: <https://josefpihrt.github.io/docs/roslynator/analyzers>.
* NUnit analyzers: <https://github.com/nunit/nunit.analyzers>.
* .NET analyzer rules (CA*): <https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/>.
