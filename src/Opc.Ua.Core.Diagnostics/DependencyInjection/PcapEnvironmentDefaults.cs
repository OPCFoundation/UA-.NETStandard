// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Pcap.DependencyInjection
{
    /// <summary>
    /// Reader that resolves
    /// <see cref="PcapEnvironmentVariableNames.OpcuaPcapFile"/> and
    /// <see cref="PcapEnvironmentVariableNames.OpcuaKeyLogFile"/> from
    /// either the process environment or a caller-supplied lookup
    /// delegate (used by tests to avoid mutating
    /// <see cref="Environment.SetEnvironmentVariable(string, string)"/>).
    /// </summary>
    /// <remarks>
    /// Whitespace-only values are treated as unset so accidentally
    /// assigning the variable to an empty string does not silently
    /// activate auto-capture.
    /// </remarks>
    internal static class PcapEnvironmentDefaults
    {
        /// <summary>
        /// Reads the env-var defaults using
        /// <see cref="Environment.GetEnvironmentVariable(string)"/>.
        /// </summary>
        public static PcapEnvironmentSnapshot ReadFromEnvironment()
        {
            return ReadFromEnvironment(static name => Environment.GetEnvironmentVariable(name));
        }

        /// <summary>
        /// Reads the env-var defaults using a caller-supplied lookup
        /// delegate. The delegate is invoked once per variable.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="getEnvironmentVariable"/> is <c>null</c>.
        /// </exception>
        public static PcapEnvironmentSnapshot ReadFromEnvironment(
            Func<string, string?> getEnvironmentVariable)
        {
            ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

            string? pcap = Normalize(
                getEnvironmentVariable(PcapEnvironmentVariableNames.OpcuaPcapFile));
            string? keylog = Normalize(
                getEnvironmentVariable(PcapEnvironmentVariableNames.OpcuaKeyLogFile));
            return new PcapEnvironmentSnapshot(pcap, keylog);
        }

        private static string? Normalize(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    /// <summary>
    /// Immutable snapshot of the env-var values relevant to the
    /// Pcap binding's auto-start path.
    /// </summary>
    /// <param name="PcapFilePath">
    /// Resolved value of
    /// <see cref="PcapEnvironmentVariableNames.OpcuaPcapFile"/>; <c>null</c>
    /// when the variable is unset or whitespace-only.
    /// </param>
    /// <param name="KeyLogFilePath">
    /// Resolved value of
    /// <see cref="PcapEnvironmentVariableNames.OpcuaKeyLogFile"/>; <c>null</c>
    /// when the variable is unset or whitespace-only.
    /// </param>
    internal readonly record struct PcapEnvironmentSnapshot(
        string? PcapFilePath,
        string? KeyLogFilePath)
    {
        /// <summary>
        /// <c>true</c> when at least one variable is set.
        /// </summary>
        public bool HasAny => PcapFilePath is not null || KeyLogFilePath is not null;

        /// <summary>
        /// <c>true</c> when only the keylog variable is set; the
        /// stand-alone keylog path runs in this case.
        /// </summary>
        public bool IsKeyLogOnly => PcapFilePath is null && KeyLogFilePath is not null;
    }
}
