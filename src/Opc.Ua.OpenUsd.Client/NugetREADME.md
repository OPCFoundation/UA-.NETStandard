# Opc.Ua.OpenUsd.Client

Generic, reusable **client-side connector** for the draft OPC UA – OpenUSD Bindings companion model.

Given an OPC UA `ISession` and a pluggable `IUsdSink`, the `OpenUsdConnector`:

- discovers a server's `OpenUsdRepresentation` registry and its live / alarm / history / command bindings;
- subscribes to the sources, applies the declared conversion (`Scale`/`Offset`, engineering units, render-target semantics) and writes into the sink;
- composes component prims (§5.12–5.14) and, when the server serves them (§5.15), streams, verifies and caches the USD asset closure;
- optionally, and fail-closed, issues authorized command writes.

It depends only on `Opc.Ua.Client`, so it works with **any** server exposing the OpenUSD binding, independent of the domain model. Two `IUsdSink` implementations ship in the box: `MockUsdSink` (in-memory, for tests) and `UsdFileSink` (authors a text `live.usda` override layer).

Host it with the thin `Opc.Ua.OpenUsd.Connector` command-line tool, or inject it into your own application.
