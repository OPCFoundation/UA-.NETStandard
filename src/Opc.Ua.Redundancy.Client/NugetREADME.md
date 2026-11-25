<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

# OPCFoundation.NetStandard.Opc.Ua.Redundancy.Client

Coordinates OPC UA client replicas so the elected client can resume work.
With token reuse enabled, the server connection must use a signed UA
SecureChannel and verify the independently provisioned server application URI.

Use an authenticated transport and a record protector for shared session
handoffs. To detect rollback of the payload store, pass an enrolled
`FreshnessProtectedKeyValueStore` backed by a separately administered authority
to the replica coordinator. CRDT gossip alone does not establish freshness.
See the [High Availability guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/HighAvailability.md).
