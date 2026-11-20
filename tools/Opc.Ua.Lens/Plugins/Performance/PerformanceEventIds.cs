// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua;

internal static partial class UaLensEventIds
{
    /// <summary>
    /// Performance owns the 2100-2199 sub-range of the Diagnose tools' reserved
    /// 2000-2999 block (Subscription Bench owns 2000-2099).
    /// </summary>
    public const int PerformanceRunStarted = 2100;
    public const int PerformanceRunFinished = 2101;
    public const int PerformanceStopFailed = 2102;
    public const int PerformanceExportFailed = 2103;
    public const int PerformanceSaveFailed = 2104;
    public const int PerformanceLoadFailed = 2105;
    public const int PerformanceRestoreFailed = 2106;
    public const int PerformanceRunFailed = 2107;
}
