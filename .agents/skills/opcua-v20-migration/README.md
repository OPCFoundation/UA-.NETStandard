# OPC UA 1.5.378 → 2.0 Migration Skill

A portable [Agent Skill](https://agentskills.io/specification) that packages the
OPC UA .NET Standard 1.5.378 → 2.0 migration knowledge so any Skill-compatible
runtime (Microsoft Agent Framework `AgentSkillsProvider` / `SkillsProvider`,
Anthropic Claude Code, Anthropic API, etc.) can load it on demand.

## When to use

Trigger this skill when a user asks for any of:

- "migrate to v20" / "update from master378" / "fix v20 build errors"
- "modernize Variant / ArrayOf / DateTimeUtc / ByteString APIs"
- "fix CS0246 on `<Type>Collection` wrappers"
- "address `UA0001`–`UA0022` analyzer warnings"
- "resolve `MIG01` from the source generator"
- "how do I install `OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer`"

## When NOT to use

- The user is starting a new OPC UA project from scratch — point them at
  `docs/README.md` and the `samples/ConsoleReferenceClient` /
  `ConsoleReferenceServer` samples instead.
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
│   ├── analyzer-rules.md                         # Full UA0001-UA0022 + MIG01 reference
│   ├── source-generator.md                       # MigrationGenerator deep-dive + MIG01 playbook
│   ├── runtime-shim.md                           # Opc.Ua.MigrationAnalyzer.Core coverage
│   ├── migration-patterns.md                     # 14-section categorical playbook
│   ├── known-gaps.md                             # Legacy WinForms, Quickstarts.Servers, CS0050
│   └── compatibility-matrix.md                   # SDK / TFM / Roslyn API requirements
├── scripts/
│   └── apply-codefixes.ps1                       # dotnet format analyzers wrapper
└── assets/
    ├── PackageReference.example.xml
    └── Directory.Build.targets.example.xml       # NoWarn recipe for TreatWarningsAsErrors
```

## Canonical upstream docs

This skill **distils** the following authoritative repo files. When you update
the skill, also update them (and vice versa) so the views stay in sync:

- [`docs/MigrationGuide.md`](../../../docs/MigrationGuide.md) — the migration
  guide landing page (small; just an index across versions).
- [`docs/migrate/2.0.x/README.md`](../../../docs/migrate/2.0.x/README.md) —
  the 2.0 version landing page + the same symptom → sub-doc table this skill
  uses to load only what's needed.
- [`docs/migrate/2.0.x/`](../../../docs/migrate/2.0.x/) — the 12 thematic
  sub-docs (telemetry, packages, source-generation, types, encoders,
  node-states, identity, certificates, configuration, sessions-subscriptions,
  alarms-model-change, timeprovider).
- [`tools/Opc.Ua.MigrationAnalyzer/NugetREADME.md`](../../../tools/Opc.Ua.MigrationAnalyzer/NugetREADME.md)
  — the package's own NuGet README.

## License

MIT — same as the parent OPC UA .NET Standard repo
([LICENSE.txt](../../../LICENSE.txt)).
