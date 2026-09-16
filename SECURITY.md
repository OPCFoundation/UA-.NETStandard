# Security Policy

## Supported Versions

This is the canonical release-line and security-maintenance matrix for this repository.
Use the latest patch or current build within the applicable maintained line; maintenance
does not mean that every historical patch receives a separate backport.

| Release line | Maintenance target | Security-maintenance status |
| --- | --- | --- |
| `1.5.378.x` on [`master378`](https://github.com/OPCFoundation/UA-.NETStandard/tree/master378) | Latest published patch of `1.5.378.x` | Maintained for security and critical-bug fixes. New feature work is on `master`. |
| Current `2.0` on [`master`](https://github.com/OPCFoundation/UA-.NETStandard/tree/master) | Latest official `2.0.0-preview.N` release and current rolling development builds | Active development and vulnerability handling. Fixes are delivered through current releases/builds; previews and rolling builds are not stable releases or individually maintained historical branches. |
| Earlier release lines and superseded patches/previews | Upgrade to the applicable maintained line or current patch/build | Not separate maintenance targets. Reports still enter the confidential assessment process below. |

The Security WG and maintainers assess affected versions, remediation and backports
case by case within this matrix. Consult the relevant security bulletin for actual
affected/fixed versions and mitigations. This policy does not establish a patch SLA,
fixed support duration, EOL date, or a guarantee to patch every affected historical API.
See the [Migration Guide](docs/MigrationGuide.md) for upgrade guidance.

The [release-evidence contract](docs/ReleaseEvidence.md) is **active for current
`master`/2.0**, with controls required for in-scope stable releases. Missing evidence
or required production setup blocks stable publication; the policy setting does
not establish publisher isolation. Preview/development release channels retain
advisory applicability for these controls. Pipeline backport to `master378`/1.5
is deferred without changing its maintenance status. Existing mandatory checks
remain mandatory.

## Reporting a Vulnerability

The OPC Foundation publishes security bulletins that affect software that it maintains or distributes. In many cases these bulletins will affect code that OPC vendors incorporate into their products. As a result, vendors will have to patch their products to address the vulnerabilities identified.

All the bulletins that have been published are available [here](https://opcfoundation.org/security-bulletins/).

Any vulnerabilities or security concerns should be reported to ‘securityteam AT opcfoundation DOT org’.
A PGP key to encrypt any sensitive security report can be found [here](https://opcfoundation.org/SecurityBulletins/securityteam_public_key.txt).

Complete information can be found [here](https://opcfoundation.org/security/).

Follow the Foundation's [Security WG disclosure and advisory policy](https://github.com/OPCFoundation/OPC-SecurityAdvisories).
Do not post undisclosed vulnerability details, exploit inputs or sensitive logs in
public issues, pull requests or CI artifacts. Coordinate any public fix or regression
asset with the Security WG before publication.

All security reports must be assessed through this process, including reports affecting
older releases or APIs marked `[Obsolete]`. Intake and triage are distinct from a
maintenance or backport decision; obsolescence alone is not a reason to reject a report.
A GCVE, CVE or GHSA identifier is not required. Include any known identifiers as
references without treating the different identifier schemes as interchangeable.

## OSS stewardship

The [Security Stewardship annex](docs/SecurityStewardship.md) maps the Foundation's
OSS steward duties under Article 24 of the Cyber Resilience Act to this repository.
The application date is **11 December 2027**. The annex builds on the existing
Foundation process; its programme approval, role assignments and operational-readiness
records are **PENDING**, not evidence of an approved or operating compliance programme.
