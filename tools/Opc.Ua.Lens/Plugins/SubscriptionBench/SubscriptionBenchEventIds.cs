// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua;

internal static partial class UaLensEventIds
{
    /// <summary>
    /// The Diagnose tools reserve event IDs 2000-2999. Subscription Bench owns
    /// the 2000-2099 sub-range; Performance owns 2100-2199.
    /// </summary>
    public const int SubscriptionBenchConvergeFailed = 2000;
    public const int SubscriptionBenchEngineUnavailable = 2001;
    public const int SubscriptionBenchSubscriptionDisposeFailed = 2002;
    public const int SubscriptionBenchItemAddFailed = 2003;
    public const int SubscriptionBenchStopped = 2004;
    public const int SubscriptionBenchSubtreeWalkFailed = 2005;
    public const int SubscriptionBenchRestoreFailed = 2006;
}
