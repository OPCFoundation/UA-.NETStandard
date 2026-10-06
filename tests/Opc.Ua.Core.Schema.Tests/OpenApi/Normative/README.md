# Normative OpenAPI documents (test resources)

`opc.ua.openapi.allservices.json` and `opc.ua.openapi.sessionless.json` are the
OpenAPI mapping of the OPC UA services (OPC 10000-6, G.3, `info.version` 1.5.7)
that the OPC Foundation model compiler publishes, copied unchanged from
[`UA-Nodeset/OpenApi`](https://github.com/OPCFoundation/UA-Nodeset/tree/latest/OpenApi)
at commit `4b79bcfaaa44929d8b50158d25b2f57d86aed5e8` (2026-08-18).

They are embedded in the test assembly only. The REST binding does not ship
them: it generates its document from the routes and types in code
(`WebApiOpenApiGenerator`), and `WebApiOpenApiConformanceTests` compares the
generated document with these files.

To move to a newer publication, replace the two files, update the commit above
and run the tests in `OpenApi/`. A failing test names the path, schema or
property that changed.
