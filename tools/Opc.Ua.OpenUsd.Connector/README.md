# OpenUSD connector security choices

The connector's existing `--insecure` flag is a **broad, isolated-test opt-in**,
not a trust-only switch. It requests an unsecured OPC UA endpoint (falling back
to the least-secure available endpoint if None is unavailable), enables
untrusted-certificate acceptance, and installs a callback accepting **any
server-certificate validation error**, not just `BadCertificateUntrusted`.
The warning is written to standard error before certificate setup or connection.
Omitting the flag retains secured endpoint selection and certificate validation.

With `--federate`, the same choice applies to additional sessions opened for
server-provided component bindings. Only enable federation for a trusted source
of those endpoint URLs. `--insecure` does not enable command writes:
`--enable-commands` remains an independent opt-in.

The flag's behavior is intentionally unchanged. Unlike the VisualInspectionCell
server's trust-only `--insecure`, this connector option can remove message
security and bypass certificate errors other than unknown trust. Do not use it
for production connections.
