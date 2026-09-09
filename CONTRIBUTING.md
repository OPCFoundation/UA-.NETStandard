## Contributing

We strongly encourage community participation and contribution to this project.
For ordinary changes, fork the repository, commit your changes there and open a
pull request. For security-sensitive contributions, follow the confidential
process below before making a report or change public.

You must agree to the contributor license agreement before we can accept your changes. The CLA and "I AGREE" button is automatically displayed when you perform the pull request. You can preview CLA [here](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf).

### Security-sensitive contributions

For an undisclosed vulnerability or security concern, follow
[SECURITY.md](SECURITY.md#reporting-a-vulnerability) before opening a public issue
or pull request. The Foundation Security WG coordinates assessment, disclosure and
public fixes. Do not upload sensitive reproduction inputs, exploit details, case
records or logs to public discussions or CI artifacts. This applies to obsolete
APIs and older versions too; no GCVE, CVE or GHSA identifier is needed for intake.
Triage is separate from remediation/backport decisions under the
[maintenance matrix](SECURITY.md#supported-versions).

The [Security Stewardship annex](docs/SecurityStewardship.md) describes secure-development
and vulnerability-handling records, community information-sharing and developers'
voluntary Article 15 reporting. These build on the existing confidential process,
not a new public disclosure channel.

### Validation and release evidence

Describe the validation actually performed and any applicable checks not run;
only check PR checklist items that you have verified. Documentation-only changes
need local link and whitespace checks, not a build. Existing required CI checks
still apply.

Changes affecting release assurance follow the [Release Evidence contract](docs/ReleaseEvidence.md).
The new controls are a current `master`/2.0 **pilot**, not a declaration of complete
evidence or platform enforcement. Missing/failed new evidence must be visible.
After separate reviewed, protected graduation, only in-scope stable releases require
the new controls; preview/development evidence stays advisory. Do not weaken existing
mandatory signing, build, test or security checks. The 1.5 pipeline rollout is
deferred without changing its maintenance status.

### Continuous integration on your pull request

Builds are not started automatically for pull requests from outside contributors — including those opened by the GitHub Copilot coding agent. A maintainer reviews the change first and then comments `/azp run` to start the Azure Pipelines validation build, and approves the GitHub Actions workflows. If your pull request shows no checks yet, this is expected; please wait for a maintainer rather than pushing empty commits.

See [Continuous integration](docs/DeveloperGuide.md#continuous-integration) in the developer guide for what the pipelines run, and for the coverage gates your change has to satisfy.
