# OPC UA 1.5.378 → 2.0 Migration Skill

A portable [Agent Skill](https://agentskills.io/specification) that packages the
OPC UA .NET Standard 1.5.378 → 2.0 migration knowledge so any Skill-compatible
runtime (Microsoft Agent Framework `AgentSkillsProvider` / `SkillsProvider`,
Anthropic Claude Code, Anthropic API, etc.) can load it on demand.

## Install with GitHub Copilot CLI

Register this repository as a marketplace, then install the plugin:

```bash
copilot plugin marketplace add OPCFoundation/UA-.NETStandard
copilot plugin install opcua-v20-migration@opcua-dotnet
```

The skill is already discovered automatically when Copilot CLI runs inside a
clone of this repository.

## When to use

Trigger this skill when a user asks for any of:

- "migrate to v20" / "update from master378" / "fix v20 build errors"
- "modernize Variant / ArrayOf / DateTimeUtc / ByteString APIs"
- "fix CS0246 on `<Type>Collection` wrappers"
- "address the 26 analyzer rules through `UA0030` or the `UA0029` shim marker"
- "resolve `MIG01` from the source generator"
- "how do I install `OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer`"

## When NOT to use

- The user is starting a new OPC UA project from scratch — point them at
  the [documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/README.md)
  and the
  [Console Reference Client](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/samples/Reference/ConsoleReferenceClient) /
  [Console Reference Server](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/samples/Reference/ConsoleReferenceServer)
  samples instead.
- The user is migrating **within** 1.5.x (point or service-release upgrades).
- The user is debugging server-side OPC UA functional behaviour — try the
  `opcua-interop-tester` skill / agent instead.
- The user is upgrading from a release older than 1.5.378 — there is no
  documented direct path; advise an interim hop to 1.5.378 first.

## Layout

```
opcua-v20-migration/
├── SKILL.md                                      # Entry point. Levels 1 + 2. < 5K tokens.
├── README.md                                     # This file.
├── references/
│   ├── package-install.md                        # PackageReference + dotnet format
│   ├── analyzer-rules.md                         # Implemented UA rules, UA0029 marker + MIG01
│   ├── source-generator.md                       # MigrationGenerator deep-dive + MIG01 playbook
│   ├── runtime-shim.md                           # Opc.Ua.MigrationAnalyzer.Core coverage
│   ├── migration-patterns.md                     # 14-section categorical playbook
│   ├── known-gaps.md                             # Legacy WinForms, shim lifetime, analyzer loading
│   ├── compatibility-matrix.md                   # SDK / TFM / Roslyn API requirements
│   └── stack-migration/                          # Bundled snapshot of all 15 thematic migration docs
├── scripts/
│   └── apply-codefixes.ps1                       # dotnet format analyzers wrapper
└── assets/
    ├── PackageReference.example.xml
    └── Directory.Build.targets.example.xml       # NoWarn recipe for TreatWarningsAsErrors
```

## Bundled and upstream docs

The operational workflow uses the bundled
[`references/stack-migration/`](references/stack-migration/README.md) snapshot,
so an installed plugin works offline and does not depend on mutable `master`.
When changing the repository's authoritative `docs/migrate/2.0.x/*.md` files,
refresh the bundled copies in the same change with
`./.azurepipelines/validate-migration-plugin.ps1 -Update`. CI runs the same
script without `-Update` to reject drift and mismatched marketplace metadata.

The current upstream files remain useful for checking changes made after the
installed plugin version:

- [`docs/MigrationGuide.md`](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md) —
  the migration guide landing page and cross-cutting migration notes.
- [`docs/migrate/2.0.x/README.md`](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/migrate/2.0.x/README.md) —
  the 2.0 version landing page + the same symptom → sub-doc table this skill
  uses to load only what's needed.
- [`docs/migrate/2.0.x/`](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/docs/migrate/2.0.x) —
  the thematic sub-doc collection. Its landing page is the source of truth for
  the current inventory.
- [`tools/Opc.Ua.MigrationAnalyzer/NugetREADME.md`](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/tools/Opc.Ua.MigrationAnalyzer/NugetREADME.md)
  — the package's own NuGet README.

## License

MIT — same as the parent OPC UA .NET Standard repo
([LICENSE.txt](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/LICENSE.txt)).
