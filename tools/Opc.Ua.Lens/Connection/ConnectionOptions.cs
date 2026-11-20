/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
    public bool UseSecurity { get; init; } = true;
    public SubscriptionEngineKind Engine { get; init; } = SubscriptionEngineKind.ChannelV2;
}
