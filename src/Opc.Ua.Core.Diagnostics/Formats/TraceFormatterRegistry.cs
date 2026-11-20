// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Formats
{
    /// <summary>
    /// Registry for trace formatters keyed by output format.
    /// </summary>
    public sealed class TraceFormatterRegistry
    {
        private readonly Dictionary<FormatKind, ITraceFormatter> m_formatters;

        /// <summary>
        /// Constructs a formatter registry from the supplied formatters.
        /// </summary>
        public TraceFormatterRegistry(IEnumerable<ITraceFormatter> formatters)
        {
            ArgumentNullException.ThrowIfNull(formatters);

            m_formatters = formatters.ToDictionary(static formatter => formatter.Kind);
            Available = new HashSet<FormatKind>(m_formatters.Keys);
        }

        /// <summary>
        /// Gets all registered format kinds.
        /// </summary>
        public IReadOnlySet<FormatKind> Available { get; }

        /// <summary>
        /// Creates the default registry containing every built-in formatter.
        /// </summary>
        public static TraceFormatterRegistry CreateDefault()
        {
            return new TraceFormatterRegistry(
            [
                new PcapFormatter(),
                new PcapNgFormatter(),
                new JsonFormatter(),
                new CsvFormatter(),
                new TextFormatter(),
                new ServiceTimelineFormatter()
            ]);
        }

        /// <summary>
        /// Gets the formatter for the requested format kind.
        /// </summary>
        /// <exception cref="PcapDiagnosticsException"></exception>
        public ITraceFormatter Get(FormatKind kind)
        {
            if (m_formatters.TryGetValue(kind, out ITraceFormatter? formatter))
            {
                return formatter;
            }

            throw new PcapDiagnosticsException($"No formatter registered for {kind}.");
        }
    }
}
