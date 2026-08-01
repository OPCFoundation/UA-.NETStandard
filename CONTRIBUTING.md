## Contributing

We strongly encourage community participation and contribution to this project. First, please fork the repository and commit your changes there. Once happy with your changes you can generate a 'pull request'.

You must agree to the contributor license agreement before we can accept your changes. The CLA and "I AGREE" button is automatically displayed when you perform the pull request. You can preview CLA [here](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf).

### Continuous integration on your pull request

Builds are not started automatically for pull requests from outside contributors — including those opened by the GitHub Copilot coding agent. A maintainer reviews the change first and then comments `/azp run` to start the Azure Pipelines validation build, and approves the GitHub Actions workflows. If your pull request shows no checks yet, this is expected; please wait for a maintainer rather than pushing empty commits.

See [Continuous integration](docs/DeveloperGuide.md#continuous-integration) in the developer guide for what the pipelines run, and for the coverage gates your change has to satisfy.
