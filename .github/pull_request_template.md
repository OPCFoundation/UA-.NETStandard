# Description

_Describe the changes here to communicate to the maintainers why they should accept this pull request. By default - this will become the Commit message after merging and thus define history._

**Undisclosed vulnerability?** Use the [confidential Foundation process](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/SECURITY.md#reporting-a-vulnerability)
instead of a public issue or PR. Do not include exploit details, sensitive inputs,
logs or private case references here. Coordinate public fixes and regression assets
with the Security WG first, including for obsolete APIs. No GCVE/CVE/GHSA identifier
is required for intake.

## Related Issues

_Reference public issues this PR addresses. For ordinary changes, open an issue if
needed; do not create a public issue or link a restricted case for an undisclosed
vulnerability._

_For a non-confidential large or complex change, discuss the design in the related
tracking issue and obtain sign-off (the Architectural Decision Record, ADR).
For undisclosed security changes, use Security WG coordination instead of public
design discussion._

- Fixes #github-issue-number, ...

## Checklist

_Put an `x` only in boxes you have verified. Describe applicable checks not run and
non-applicable items under Validation below. Documentation-only changes need local
link and whitespace checks, not a build; existing required CI checks still apply._

- [ ] I have signed the [CLA](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf) and read the [CONTRIBUTING](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/CONTRIBUTING.md) doc.
- [ ] I have added tests that prove my fix is effective or that my feature works and increased code coverage.
- [ ] I have added all necessary documentation.
- [ ] I have verified that my changes do not introduce (new) build or analyzer warnings.
- [ ] I ran **all** tests locally using the **UA.slnx** solution against at least .net **framework** and .net **10**, and all passed.
- [ ] I fixed **all** failing and flaky tests in the CI pipelines and **all** CodeQL warnings.
- [ ] I have addressed **all** PR feedback received.

## Validation

_List checks actually run and their results, plus applicable checks not run.
Use only sanitized information; do not attach restricted findings or raw sensitive logs._

_If release evidence is affected, identify the artifact group and contract changes
under [Release Evidence](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/ReleaseEvidence.md), including unmet controls.
The contract is active for current `master`/2.0: missing evidence or required
production setup blocks in-scope stable publication. Preview/development release
channels retain advisory applicability for these controls, not a different
contract maturity mode. Existing mandatory checks must not be weakened._
