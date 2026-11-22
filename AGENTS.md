<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

# Agent instructions

The full coding guidelines for this repository are in
[`.github/copilot-instructions.md`](.github/copilot-instructions.md) and
[`docs/DeveloperGuide.md`](docs/DeveloperGuide.md). Follow them.

## License header

ALWAYS start every new or edited file with the SPDX license header, and
nothing else above it (except a `#!` line):

```csharp
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
```

- Use the file type's own comment syntax: `//` for C#, C, JavaScript, Java,
  Go, Rust; `#` for PowerShell, shell, YAML, Python; `@REM` for `.cmd`;
  `/* */` for CSS and USDA; `<!-- -->` for XML, XSD, AXAML, HTML, SVG and
  Markdown banners.
- When you touch a file that still has an old header (a `/* ===` block,
  `// ====` delimiter lines, "OPC Foundation MIT License 1.00",
  "The OPC Foundation, Inc.", an RCL/GPL notice, or a Microsoft or other
  third-party banner), replace it with the two SPDX lines. Do not add `====`
  delimiter lines or the license text.
- Never change the header of a vendored NodeSet without updating every
  SHA-256 / git-blob pin that hashes it (provenance.json,
  companion-models.json, model classes, READMEs, tests).
- NuGet packages declare `PackageLicenseExpression` `MIT` (set once in
  `common.props`). Never add `PackageLicenseFile` or pack `LICENSE.txt`.
