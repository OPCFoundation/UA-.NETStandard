// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Opc.Ua.Pcap.Tests
{
    /// <summary>
    /// Minimal <see cref="ITelemetryContext"/> for unit tests: routes all
    /// logging to <see cref="NullLoggerFactory.Instance"/> and uses
    /// dedicated activity / meter sources so production diagnostics are
    /// not contaminated.
    /// </summary>
    internal sealed class TestTelemetryContext : ITelemetryContext
    {
        public static TestTelemetryContext Instance { get; } = new();

        public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;

        public ActivitySource ActivitySource { get; } = new("Opc.Ua.Core.Diagnostics.Tests");

        public Meter CreateMeter()
        {
            return new Meter("Opc.Ua.Core.Diagnostics.Tests");
        }

        public Meter CreateMeter(System.Reflection.Assembly assembly)
        {
            return CreateMeter();
        }

        public ActivitySource GetActivitySource(System.Reflection.Assembly assembly)
        {
            return ActivitySource;
        }
    }
}
