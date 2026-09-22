## Contributing

We strongly encourage community participation and contribution to this project. First, please fork the repository and commit your changes there. Once happy with your changes you can generate a 'pull request'.

You must agree to the contributor license agreement before we can accept your changes. The CLA and "I AGREE" button is automatically displayed when you perform the pull request. You can preview CLA [here](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf).

### Continuous integration on your pull request

Builds are not started automatically for pull requests from outside contributors — including those opened by the GitHub Copilot coding agent. A maintainer reviews the change first and then approves the GitHub Actions workflows, which run the build and test validation. If your pull request shows no checks yet, this is expected; please wait for a maintainer rather than pushing empty commits.

The required check is **`build-and-test summary`**. While the Azure Pipelines definition is still being retired a maintainer may additionally comment `/azp run` to start it.

See [Continuous integration](docs/DeveloperGuide.md#continuous-integration) in the developer guide for what CI runs, how to reproduce a failing leg locally, and the coverage gates your change has to satisfy.

### Releasing

Maintainers cutting a release branch, shipping a patch or minor version, backporting a fix to a release branch, or promoting a stable package to nuget.org must follow [docs/ReleaseProcess.md](docs/ReleaseProcess.md). Releases are only ever produced from a canonical `release/<major>.<minor>` branch, never from `master`.
