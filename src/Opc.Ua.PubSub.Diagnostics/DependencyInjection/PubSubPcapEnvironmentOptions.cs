/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PubSub.Pcap.DependencyInjection
{
    /// <summary>
    /// Resolved environment-driven PubSub capture configuration, populated
    /// once at registration time from the
    /// <see cref="PubSubPcapEnvironmentVariableNames"/> variables.
    /// </summary>
    /// <param name="PcapFilePath">
    /// Destination pcap / pcapng path, or <see langword="null"/> when no
    /// capture file is configured.
    /// </param>
    public sealed record PubSubPcapEnvironmentOptions(
        string? PcapFilePath)
    {
        /// <summary>
        /// Whether an env-var driven capture should be auto-started.
        /// </summary>
        public bool IsEnabled => PcapFilePath is not null;
    }
}
