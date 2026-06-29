# OPCFoundation.NetStandard.Opc.Ua.Client.Redundancy

Client-side high availability for OPC UA: run two or more client processes as a replica set so exactly one leader holds the active session and subscriptions while followers stand by (cold, warm, or hot) and take over on leader loss. Builds on the shared `Opc.Ua.Redundancy` seams (`ISharedKeyValueStore`, `IRecordProtector`, `ILeaderElection`) and reuses `HotAndMirrored` server-side session mirroring for token-reuse fast activation.

See `Docs/HighAvailability.md` for the design and usage.
