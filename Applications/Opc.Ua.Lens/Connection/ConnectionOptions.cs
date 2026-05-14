/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

namespace UaLens.Connection;

internal enum SubscriptionEngineKind
{
    Classic,
    ChannelV2
}

internal sealed record ConnectionOptions
{
    public required string EndpointUrl { get; init; }
    public bool UseSecurity { get; init; }
    public SubscriptionEngineKind Engine { get; init; } = SubscriptionEngineKind.ChannelV2;
}
