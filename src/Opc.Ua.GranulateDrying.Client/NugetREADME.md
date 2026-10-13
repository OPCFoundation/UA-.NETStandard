# OPC UA Granulate Drying Client

Typed client support based on OPC 40082-6 version 1.0.0.

- Discover real drying devices through Machinery, including derived device types.
- Use generated typed methods for bin/bed controls, users, errors, timers, energy, and attributed maintenance.
- Read and write selected variables without discarding absence, quality, source timestamps, or engineering units.
- Download bounded maintenance instructions with generated FileType methods.
- Decode generated maintenance and common alarm records through existing streaming subscriptions.
- Reuse an existing `ManagedSession` or the DI-managed session factory.

The client does not own its supplied session and does not substitute optimistic values for device feedback.
No certification claim is made.

See [Granulate drying documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/GranulateDrying.md)
for examples, optional-feature semantics, and the full capability matrix.
