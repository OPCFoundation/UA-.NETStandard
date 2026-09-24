# OnboardingRegistrar

Hosts the OPC 10000-21 registrar administration object and injects a
`MemoryTicketStore`. Trusted client certificates and secure endpoints are the
default. Authenticated ticket administration requires the generated
`RegistrarAdmin` role; anonymous bootstrap sessions have no ticket permissions.

See the [onboarding guide](../README.md) for verified mutual-trust provisioning,
the `ONBOARDING_DEMO_USER` / `ONBOARDING_DEMO_PASSWORD` environment contract and
complete commands. `--auto-accept` permits unknown application certificates only
after explicit controlled-bootstrap consent. It warns, retains encrypted
channels and all role/ticket checks, and is independent of administrator
authentication. No None option exists. `--help` initializes no PKI or listener.
