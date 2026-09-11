# OnboardingClient

Uses the DI-provided onboarding client and generated model APIs to register and
unregister tickets. Connections always select `SignAndEncrypt` /
`Basic256Sha256`; unknown registrar certificates are rejected by default.

See the [onboarding guide](../README.md) for verified mutual-trust provisioning,
the `ONBOARDING_DEMO_USER` / `ONBOARDING_DEMO_PASSWORD` environment contract and
complete commands. `--auto-accept` is explicit controlled-bootstrap consent,
with a warning to verify the registrar before sending credentials or tickets.
It does not enable None or authorize anonymous callers. `--anonymous true`
demonstrates denied ticket administration. `--help` initializes no PKI.
