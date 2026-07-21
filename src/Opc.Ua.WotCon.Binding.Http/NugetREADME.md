# OPC UA WoT Connectivity — HTTP Executor

`OPCFoundation.NetStandard.Opc.Ua.WotCon.Binding.Http` executes HTTP / HTTPS
WoT Connectivity binding forms compiled by the HTTP planner in
`Opc.Ua.WotCon.Binding`.

It provides an `HttpClient`-based executor for read / write / action / observe /
event operations with bounded timeouts and payload sizes, cooperative
cancellation, HTTP-to-`StatusCode` mapping, an injectable client factory and
credential-provider-driven authentication headers.

## Redirect-safe credentials

- The executor-owned client disables automatic redirects and applies a bounded,
  origin-aware redirect policy. Custom header and query credentials are dropped
  whenever a redirect crosses to a different origin, redirect loops and non
  `http(s)` schemes are refused, an `https`&nbsp;&rarr;&nbsp;`http` downgrade is
  refused (unless `AllowInsecureRedirectDowngrade` is set) and the number of
  redirects is capped by `MaxAutomaticRedirects` (default 5).
- A caller-supplied `HttpClient` used with a credential-bearing form fails closed
  unless `HttpWotBindingOptions.CallerClientHandlesRedirectSafety` is set to
  confirm the client disables automatic redirects, or follows them without
  forwarding credentials across origins.

Register it with `builder.AddHttpWotBinding()`.
