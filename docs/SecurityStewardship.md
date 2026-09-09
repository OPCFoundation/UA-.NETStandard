# Security stewardship

## Scope and status

This annex concerns the OPC Foundation in its **open-source software steward**
capacity for the OPC UA .NET Standard Stack. That role is the scope of this
document, not a classification awaiting investigation. All three paragraphs of
Article 24 of Regulation (EU) 2024/2847 (the Cyber Resilience Act, CRA) apply from
**11 December 2027**, including the scoped reporting duties.[^law][^date]

The Foundation's published [security process](https://opcfoundation.org/security/)
and [Security WG disclosure and advisory policy](https://github.com/OPCFoundation/OPC-SecurityAdvisories)
remain the policy baseline.[^foundation] Use the confidential contact and PGP key in
[SECURITY.md](../SECURITY.md#reporting-a-vulnerability); this annex does not create
a competing intake or authorize public disclosure.

**Programme approval and operational readiness: PENDING.** The sections below
specify operating requirements and verification records. Committing this annex
is not evidence that the Foundation has approved a stewardship programme, appointed staff, verified
reporting platforms or publication protections, or completed an exercise.

| Record | Status | Evidence needed to close |
| --- | --- | --- |
| Foundation approval of this implementation annex | **PENDING** | Authorized decision, approved revision, scope and review arrangements. |
| Functional owners, deputies and escalation arrangements | **PENDING** | Controlled assignments and accepted responsibilities for the functions below. |
| Development-participation and provided-system scope | **PENDING** | Reviewed inventory with supporting facts and boundary decisions. |
| Cooperation/document retrieval arrangements | **PENDING** | Approved controlled packet, retrieval and language arrangements. |
| Steward reporting route and access | **PENDING** | Then-current operational guidance, authorized access and outage procedure confirmation. |
| Reporting and cooperation exercises | **PENDING** | Dated exercise records, outcomes and disposition of gaps; instructions alone are not completion evidence. |
| Release-evidence engineering controls | **ACTIVE; stable controls required** | See [Release Evidence](ReleaseEvidence.md); missing production trust, verification or publication-boundary records block in-scope stable publication. This does not establish Foundation programme approval. |

Article 24 does not itself impose an SBOM, CE marking, conformity assessment, a
manufacturer's minimum five-year support period or ten-year retention mandate.
This annex does not introduce any such programme, fixed patch SLA, EOL date or
fixed retention period.[^law] The [maintenance matrix](../SECURITY.md#supported-versions)
is the canonical repository maintenance statement.

## Statutory duties and implementation records

The duties in the second column are statutory. Record formats and functional
organization in the third column are implementation choices, not statutory job
titles or prescribed tooling.[^law]

| Provision | Duty in the Foundation's steward scope | Implementation and verifiable records |
| --- | --- | --- |
| **24(1): policy** | Put in place and document, in a verifiable manner, a cybersecurity policy fostering secure development and effective vulnerability handling by developers; account for the steward's specific nature and legal/organizational arrangements. | Approved versioned policy, responsibilities, review decisions, and traceable examples connecting development and handling work to the policy. |
| **24(1): handling and sharing** | Include aspects of documenting, addressing and remediating vulnerabilities; promote sharing information about discovered vulnerabilities within the open-source community; foster developers' voluntary Article 15 vulnerability reporting. | Controlled intake, assessment, affected-version and remediation decisions, coordinated advisory records, developer guidance and voluntary-reporting decisions. |
| **24(2): cooperation** | Cooperate with market surveillance authorities at their request to mitigate cybersecurity risks posed by the FOSS product. | Verified request, accountable liaison, risk-mitigation actions and response correspondence. |
| **24(2): documentation** | Following a reasoned request, supply the paragraph 1 documentation in paper or electronic form and a language the authority can easily understand. | Retrievable policy revisions, approval history, supporting index, language/translation arrangements and delivery record. No numerical response deadline is specified by 24(2). |
| **24(3), first sentence** | Article 14(1) applies to the extent the steward is involved in the development of the product. | Development-participation facts, active-exploitation assessment, awareness ledger and staged reporting records using 14(2). |
| **24(3), second sentence** | Articles 14(3) **and 14(8)** apply to the extent qualifying severe incidents affect network/information systems provided by the steward for development of the products. | Provided-system and product-impact assessment, incident reporting using 14(4), and timely impacted-user information within this scope. |

### Functional responsibility

These are functions to assign, not assertions of existing appointments. Keep names,
contact details, deputies, escalation/availability arrangements and approval records
in controlled systems. Choose arrangements capable of meeting the applicable clocks;
this annex does not prescribe a staffing model.

| Function | Responsibility | Primary/deputy assignment |
| --- | --- | --- |
| Foundation programme approval | Approve the policy mapping, scope, resources and review decisions. | **PENDING / PENDING** |
| Security WG handling/reporting coordination | Connect existing intake to triage, scope assessment, authority notifications, voluntary reporting and disclosure coordination. | **PENDING / PENDING** |
| Stack maintenance | Assess affected code/versions, development participation, remediation, regressions and backports. | **PENDING / PENDING** |
| Development infrastructure response | Establish provided-system boundaries, assess incidents, contain harm and preserve restricted evidence. | **PENDING / PENDING** |
| Authority liaison and records | Authenticate requests, retrieve documentation, arrange language/translation, record delivery and control access/retention. | **PENDING / PENDING** |
| User/community communications | Coordinate applicable incident-user information and wider Foundation advisories without delaying mandatory filings. | **PENDING / PENDING** |
| Release assurance | Maintain engineering evidence and report actual readiness, unmet controls and publication-boundary limitations. | **PENDING / PENDING** |

### Secure development and vulnerability handling

Use the [Developer Guide](DeveloperGuide.md) for code review, tests, dependency
approval, analyzers, audit coverage, certificate/secret handling and release
practices. Retain traceable decisions and actual results for material risks;
configuration of a scanner or the existence of a test is not evidence it ran.
Connect findings to accountable assessment and remediation rather than relying
only on aggregate build status.

For a vulnerability case, keep the received report, affected product/version facts,
assessment rationale, remediation or mitigation decisions, verification results and
disclosure decisions under the existing Security WG process. Assess security
reports even when the API is obsolete or the reported release is historical.
Triage does not promise a patch or backport: follow the
[maintenance matrix](../SECURITY.md#supported-versions) and record affected/fixed
versions and available mitigations accurately.

Intake and reporting are **identifier-agnostic**. A GCVE, CVE or GHSA identifier
may be recorded with its scheme and aliases; none is a prerequisite and the
schemes are not interchangeable. Do not wait for an identifier before assessing
scope or starting an applicable clock.

Use the Foundation's coordinated disclosure/advisory process to share reviewed
vulnerability and remediation information with the open-source community. Keep
undisclosed findings, sensitive crash inputs and raw logs out of public issues,
PRs and CI artifacts until their publication has been reviewed and coordinated.
Public regression tests must not disclose a restricted case prematurely.

Foster developers' **voluntary Article 15 vulnerability reporting** to a CSIRT
designated as coordinator or ENISA, where appropriate. Article 15 also permits
voluntary reports of cyber threats, incidents and near misses. Coordinate safe
handling with the Security WG and confirm the then-current voluntary route; do
not relabel all vulnerabilities as mandatory reports or assume an online form is
available. Voluntary reporting does not replace an applicable mandatory duty.[^law][^enisa]

## Controlled scope inventory and cooperation packet

The inventory applies the confirmed steward role to facts; it does not reopen that
role. Keep a reviewed record for each relevant product/component and development
service. Public documentation should expose non-sensitive categories and readiness
status, not exploitable infrastructure maps or confidential cases.

| Controlled record | Minimum useful content |
| --- | --- |
| Development participation | Product/component, repository/release line, nature of Foundation development involvement, evidence, responsible function, scope rationale and review date. |
| Systems provided for development | Service purpose and provider, Foundation provision/control arrangements, responsible administrators, affected products, shared/outsourced dependencies, incident relevance and boundary rationale. |
| Reporting decisions | Awareness facts/timestamps, exploitation or severe-incident criteria, relevant inventory entries, mandatory/voluntary/non-reportable rationale, reassessment and decision owner. |
| Policy/cooperation index | Approved policy revisions and history, functional contacts/deputies, inventory references, selected handling/remediation and information-sharing examples, release-evidence references, access and retrieval instructions. |
| Records handling | Classification, authorized recipients, controlled storage, accountable retention/review decisions and secure delivery procedure. |

Candidate service categories include source hosting, CI/build runners, signing and
release services, artifact storage and development collaboration systems. A service
listed in source configuration is not proof that it is provided by the Foundation
for development. Assess shared and outsourced arrangements from actual facts.
Downstream vendor installations and arbitrary external services are not
automatically steward-provided development systems.

For an authority request:

1. Authenticate the requesting authority and record the request, requested scope,
   reason and response contact through the assigned liaison.
2. Coordinate requested risk-mitigation cooperation under Article 24(2).
3. For a reasoned documentation request, retrieve the relevant paragraph 1 policy
   documentation, verify the approved revision and arrange paper/electronic
   delivery in a language the authority can easily understand.
4. Review access, confidentiality and supporting records before secure delivery;
   retain the response and delivery record. The packet is not a public download
   and the documentation duty does not require publishing raw internal cases.
5. Track any follow-up, factual gaps and corrective work. Apply the authority's
   request and applicable procedure without inventing a universal response SLA.

## Scoped reporting runbook

This runbook describes the duties applicable from **11 December 2027** and the
readiness work needed beforehand. It is not an instruction to perform a live
filing or a claim that steward access has been verified.

### Determine the route

**Actively exploited vulnerability:** require reliable evidence that a malicious
actor exploited the vulnerability in a system without its owner's permission
(Article 3(42)). A severity score, identifier, suspected weakness or authorized
proof of concept alone is not this trigger. Apply Article 14(1), through the
first sentence of 24(3), only to the extent the Foundation is involved in
development of the product. Other reports still require policy-driven assessment
and may be suitable for voluntary reporting.[^law]

**Severe incident:** require both the second-sentence Article 24(3) provided-system
condition and the Article 14(5) product-security severity test. That test covers
actual or potential impairment of the product's ability to protect availability,
authenticity, integrity or confidentiality of sensitive/important data or functions,
**or** actual or potential introduction/execution of malicious code in the product
or a user's network/information systems. An ordinary outage or downstream incident
is not automatically a reportable steward incident. Actual downstream harm need
not already have occurred when the potential-impact test is met.[^law]

**User information:** Article 14(8) is attached by Article 24(3)'s **second
sentence**, alongside 14(3), to the qualifying provided-development-system incident
scope. Do not attach it to every first-sentence exploitation report. In the
applicable incident scope, inform impacted product users and, where appropriate,
all users, with necessary risk-mitigation/corrective information; use an easily
automatically processable structured format where appropriate. Provide timely
information rather than waiting for the final authority report. Wider
vulnerability advisories remain part of community information-sharing and the
Foundation process, not an automatic expansion of this 14(8) duty.[^law]

### Keep independent clocks

The Article 14(2) and 14(4) procedures apply to the corresponding scoped
notifications above. Record when awareness occurred and the facts establishing
it; do not substitute identifier assignment, triage completion or a platform
counter for legal awareness.[^law][^enisa]

| Route | Early warning | Fuller notification | Final report |
| --- | --- | --- | --- |
| Actively exploited vulnerability | Without undue delay; within **24 hours of awareness**. | Without undue delay; within **72 hours of awareness**, unless relevant information was already supplied. | No later than **14 days after a corrective or mitigating measure is available**. |
| Severe provided-development-system incident | Without undue delay; within **24 hours of awareness**. | Without undue delay; within **72 hours of awareness**, unless relevant information was already supplied. | Within **one month after submission of the incident notification** under 14(4)(b). |

The 72-hour limit runs from awareness, **not** from the early warning. The
vulnerability-final clock starts at remedy availability, not discovery; the
incident-final clock starts at notification submission. Article 14(4)(c) gives
**no ongoing-incident extension**: report applied and ongoing mitigation within
the one-month deadline. Coordination or an advisory embargo does not extend
authority-notification clocks.

### Handle and record each stage

1. Engage the assigned lead/deputy and preserve facts in a restricted case record.
   Record development involvement, provided systems where relevant, affected
   products/versions, awareness and scope rationale. Reassess as facts change;
   uncertainty must not silently stop the deadline ledger.
2. For a mandatory route, notify the relevant coordinating CSIRT and ENISA through
   the applicable single reporting platform (SRP) procedure. Supply available
   affected-Member-State information and the information required by each stage.
   The incident early warning indicates suspected unlawful or malicious causation.
3. In the vulnerability notification, include available product/vulnerability
   information, exploitation/mitigation information and the sensitivity assessment.
   In the incident notification, include the available incident assessment,
   severity/impact, corrective/mitigating measures and sensitivity assessment.
   Preserve submitted versions, timestamps and receipts.
4. For the vulnerability final report, document the vulnerability, severity/impact,
   available malicious-actor information and corrective/mitigating measure. Record
   the measure's availability date separately.
5. For the incident final report, document severity/impact, likely threat or root
   cause and applied/ongoing mitigation. Record the fuller notification's submission
   timestamp separately; ongoing response work does not postpone this report.
6. Coordinate the applicable incident-user information and wider Foundation
   advisories as distinct communication decisions. Retain sanitized public outputs
   and restricted supporting records with appropriate access controls.

### Confirm operational readiness

Before readiness sign-off, confirm the then-current ENISA/CSIRT guidance, steward
authorization/registration, coordinating CSIRT, stage-specific forms, secure
handling, voluntary route and platform-outage procedure. Keep deadline tracking
independent of platform counters. Do not assume the platform accepts stewards or
voluntary submissions, or send a real notification merely to test access.
ENISA describes direct coordinating-CSIRT contact for immediate communication
during platform unavailability with subsequent SRP submission; confirm current
instructions without treating an outage as a deadline waiver.[^enisa]

Access, contact and submission-readiness confirmation remains **PENDING** until
authorized personnel record it. These are operational questions, not uncertainty
about the Article 24 application date.

## Tabletop and retrieval verification

The following are **exercise instructions**, not completed exercises. Use synthetic
facts in an approved controlled setting, not live exploit inputs, production
credentials or actual authority notifications.

| Scenario | Required exercise steps and verification |
| --- | --- |
| Exploitation within development involvement | Start with reliable exploitation evidence and no identifier, including an obsolete API. Record scope and awareness; prepare 24-/72-hour stages; introduce a remedy-availability event and calculate the 14-day final deadline. Keep remediation/backport decisions separate from reporting. |
| Severe incident affecting a provided development system | Use a synthetic build-integrity incident with potential malicious product code. Verify both scope and severity conditions, prepare 24-/72-hour stages and timely incident-user information, then calculate one month from notification submission. Keep the final deadline even when mitigation continues. |
| Boundary and voluntary case | Compare an authorized proof of concept and an unrelated downstream outage with mandatory triggers. Record non-reportable or voluntary rationale, continued vulnerability handling and conditions for reassessment. |
| Reasoned authority request | Retrieve the approved policy revision and controlled supporting index, arrange an understandable language and secure paper/electronic delivery, and demonstrate retrieval without exposing the packet publicly. |

Record the exercise date, participants/functions, synthetic timeline, draft outputs,
scope/deadline decisions, retrieval/language results, gaps, corrective owners and
review acceptance in controlled records. Publish only a reviewed, sanitized status.
Do not mark an exercise complete until its outcome record exists.

### Machine-readable readiness records and synthetic scenarios

[`readiness-progress.json`](../.azurepipelines/readiness-progress.json) tracks the
six organizational requirements above and references the active engineering checks
in `release-policy.json` by ID. All checked-in readiness records are **pending**;
that status does not defer the required stable engineering gates.
[`readiness-record.schema.json`](../.azurepipelines/readiness-record.schema.json)
is a separate versioned companion; it does not change signed release envelopes.
The responsible function identifies who must arrange a decision, not an existing
personal appointment.

The nonpackable release-evidence tool checks schema, complete check membership,
group/destination scope, public reference format and review intervals:

```powershell
$tool = '.\tools\Opc.Ua.ReleaseEvidence\bin\Release\net10.0\Opc.Ua.ReleaseEvidence.dll'
dotnet $tool validate-readiness --repository-root . `
  --input .\.azurepipelines\readiness-progress.json `
  --output .\readiness-validation.json
```

An exit-zero result means **structurally valid, operational readiness pending**,
not approved or exercised. `submitted` records require immutable policy/controller
identities, primary/deputy assignment references, reviewer authority, approval and
supporting record references, and a review interval. Those are still untrusted
claims: they produce `authentication-required`, or `stale` after a policy change
or expiry. `revoked` stays revoked. An `approved` status or a caller-supplied
verification Boolean is not accepted. Missing checks and malformed inputs fail.
Actual authenticated approval belongs in the protected verification process,
not this public progress template.

Use functional roles and opaque `record:` references only. Keep personal
assignments, contact details, internal locations, raw evidence hashes, case
identifiers and correspondence in the controlled records. A reference's presence
does not establish that its target exists or is authorized.

The `tabletop` command accepts **synthetic scenarios only**. For example, save
this hypothetical scenario as a runner-local JSON file, not an operational case:

```json
{
  "schemaVersion": 1,
  "synthetic": true,
  "route": "incident",
  "calendarTimeZone": "UTC",
  "providedDevelopmentSystem": "yes",
  "productSecurityImpact": "yes",
  "awareness": "2028-01-29T08:00:00Z",
  "incidentNotification": "2028-01-31T08:00:00Z"
}
```

```powershell
dotnet $tool tabletop --input .\synthetic-scenario.json --output .\synthetic-result.json
```

This example yields 24-/72-hour clocks from January 29 and a calendar-month final
report date of February 29. A vulnerability scenario instead needs
`developmentInvolvement` and `activelyExploited`, with optional `remedyAvailable`
for the separate 14-day final clock. No identifier is required. Unknown scope
retains explicitly provisional awareness clocks for review; a known failed scope
condition does not become a mandatory route. Voluntary and cooperation scenarios
do not invent mandatory deadlines; cooperation requires authenticated-request,
approved-policy, retrieval/language and delivery records.

**These are transparent exercise assumptions, not a binding legal deadline
calculator.** Timestamps require explicit offsets. Awareness hours and remedy
days use elapsed UTC time; incident months preserve local wall time in the
declared time zone, clamping to the last day of shorter months. Ambiguous or
nonexistent daylight-saving times require explicit interpretation and are
rejected rather than guessed. There is no weekend extension. Foundation review
must confirm calendar and legal interpretation, including "without undue delay".
Outputs always state that Foundation review is required and an operational
exercise has **not** been completed. The command cannot file reports or establish
platform access.

### Administrator and Foundation operating prerequisites

Supply the following through controlled, attributable records; do not publish
credentials, personal contacts or infrastructure inventories in this repository.
None of these actions is authorized merely by running the validators.

| Decision or evidence | Responsible function | Completion evidence |
| --- | --- | --- |
| Policy and controller authority | Foundation programme approval / release assurance | Approved immutable policy/controller identities, actual environment reviewers, branch restrictions and bypass settings, and independent trust/bootstrap and revocation/checkpoint provisioning. |
| Every current-line official writer | Development infrastructure response | GitHub/NuGet/GHCR grants, token and OIDC policies, preview/rolling writers, old workflows and alternate external Azure definitions/credentials. Include principals capable of bypassing a stable gate. |
| Candidate/official separation | Development infrastructure response | Selected candidate locations and visibility, isolated authority, destination scope and verified limits on candidate/source workflows. A tag prefix or named `release` environment is not isolation. |
| Deferred-line protection | Development infrastructure response / stack maintenance | Evidence that proposed grant or credential changes leave maintained 1.5 delivery unaffected. Do not revoke shared authority or expand scope to resolve an unapproved isolation conflict. |
| Producer and verifier qualification | Release assurance / stack maintenance | Approved definition/tool pins, actual source-bound runs, native execution and full analysis scope, authenticated reviewed finding dispositions, and qualification limitations. Synthetic fixtures are not live qualification. |
| Retrieval, retention and recovery | Authority liaison / release assurance | Public evidence retrieval, controlled record ownership/access, and separately authorized interrupted-delivery and recovery exercises. No fixed retention term is inferred. |
| Stewardship policy and assignments | Foundation programme approval | Approved annex revision, accepted primary/deputy assignments, escalation arrangements and revalidation triggers. |
| Development and provided-system scope | Stack maintenance / development infrastructure response | Fact-based participation and provided-system decisions, including outsourced/shared services; repository settings alone are insufficient. |
| Cooperation and reporting operation | Security WG / authority liaison / user communications | Current steward routing/access and outage procedure, language/retrieval arrangements, performed synthetic exercises and reviewed outcomes; incident-user communication remains scoped to the incident route. |

Production trust and publisher isolation require authenticated setup records;
Foundation approvals, assignments and exercises require separate operational
records. These records remain **pending**. Missing required engineering records
block stable publication under the active contract. Repository implementations
and offline fixtures cannot establish production trust or organizational
readiness, or authorize notification to authorities.

## Chosen engineering controls and release scope

SBOMs, provenance/attestations, stronger artifact verification, risk-to-test
traceability, retained release evidence and the maintenance matrix are chosen
engineering practices supporting stewardship, **not additional statutory
Article 24 deliverables**. They do not replace the verifiable policy, cooperation
or scoped reporting duties.

The [Release Evidence contract](ReleaseEvidence.md) is the engineering reference.
Its implementation scope is current **`master`/2.0 only**, covering NuGet and the
designated published container images. The `master378`/1.5 pipeline backport is
**deferred**, while 1.5 maintenance remains as stated in
[SECURITY.md](../SECURITY.md#supported-versions). Engineering enrollment scope must
not be used to exclude relevant products from the statutory scope inventory.

The contract is **active**, with `stage: required` for **in-scope stable releases**.
Missing or failed evidence remains incomplete and blocks stable publication.
Production trust and publication isolation are required operating prerequisites,
not implied by the policy setting. Official previews and rolling development
builds retain advisory applicability for these controls; those are release
channels, not maturity modes of the contract, and do not establish compliance
with the required stable profile. Existing mandatory signing, build, test and
security checks are not weakened.

Public evidence is limited to reviewed, sanitized SBOMs, provenance and summaries
under the contract. Keep raw findings, sensitive crash inputs, credentials,
infrastructure inventories, authority correspondence and operational case records
restricted. Choose accountable retention/access arrangements without importing a
manufacturer's fixed retention mandate. A workflow or environment name alone
does not prove protected publication, credential isolation or durable archiving;
administrator confirmation and actual execution records remain necessary.

## Sources

[^law]: [Regulation (EU) 2024/2847, official text](https://publications.europa.eu/resource/cellar/21b7d4eb-a6e2-11ef-85f0-01aa75ed71a1.0006.03/DOC_1), Articles 3(42), 24(1)-(3), 14(1)-(5), 14(7)-(8), 15 and 71(2). Article 24 limits the incorporated reporting and user-information duties as described above.
[^date]: [Commission-services CRA FAQ, version 1.4, 4 September 2026](https://ec.europa.eu/newsroom/dae/redirection/document/122331), section 5.5, page 55; [ENISA SRP FAQ](https://www.enisa.europa.eu/topics/product-security/single-reporting-platform-srp/frequently-asked-questions), Q29. These corroborate 11 December 2027 for Article 24, including paragraph 3; the Commission FAQ is explanatory, not additional legislation.
[^foundation]: [Foundation Security WG policy, reviewed revision](https://github.com/OPCFoundation/OPC-SecurityAdvisories/blob/11d0fc28ad4c3b9e37e26c61855fc7f2392e1c38/README.md#L2-L21). This is evidence of the published confidential intake, coordination and advisory process, not approval of this annex or proof of unobserved internal operations.
[^enisa]: [ENISA SRP FAQ](https://www.enisa.europa.eu/topics/product-security/single-reporting-platform-srp/frequently-asked-questions), Q16-19 and Q25-30, for identifiers, coordination, clocks, outages, voluntary functionality, application date and access. Consult then-current guidance before operational sign-off; a link is not evidence of tested platform access.
