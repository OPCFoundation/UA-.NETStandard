// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Globalization;
using System.Linq;

namespace Vision.VisualInspectionAgent
{
    internal sealed record VisualInspectionAgentOptions
    {
        public string ServerUrl { get; init; } = "opc.tcp://localhost:62865/VisualInspectionCell";

        public bool Insecure { get; init; }

        public VisualInspectionAgentMode Mode { get; init; } = VisualInspectionAgentMode.Scripted;

        public int Cycles { get; init; } = 3;

        public TimeSpan OperatorTimeout { get; init; } = TimeSpan.FromSeconds(10);

        public string? AIEndpoint { get; init; }

        public static VisualInspectionAgentOptions Parse(string[] args)
        {
            return new VisualInspectionAgentOptions
            {
                ServerUrl = GetOption(args, "--server") ?? "opc.tcp://localhost:62865/VisualInspectionCell",
                Insecure = HasFlag(args, "--insecure"),
                Mode = ParseMode(GetOption(args, "--mode")),
                Cycles = Math.Max(1, int.TryParse(
                    GetOption(args, "--cycles"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cycles)
                    ? cycles
                    : 3),
                OperatorTimeout = TimeSpan.FromSeconds(Math.Max(1, int.TryParse(
                    GetOption(args, "--operator-timeout"), NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int timeout)
                    ? timeout
                    : 10)),
                AIEndpoint = GetOption(args, "--ai-endpoint")
            };
        }

        private static VisualInspectionAgentMode ParseMode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return VisualInspectionAgentMode.Scripted;
            }

            // The documented spellings are hyphenated ("live-ai"), the enum members are not.
            // Silently defaulting an unrecognised mode would drop live-ai into the simulated
            // analyser, which is exactly the degraded run the mode exists to prevent.
            string candidate = value.Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal);
            if (Enum.TryParse(candidate, ignoreCase: true, out VisualInspectionAgentMode mode) &&
                Enum.IsDefined(mode))
            {
                return mode;
            }

            throw new FormatException(
                FormattableString.Invariant(
                    $"Unknown --mode '{value}'. Expected one of: scripted, live-ai, human."));
        }

        private static string? GetOption(string[] args, string name)
        {
            for (int ii = 0; ii < args.Length - 1; ii++)
            {
                if (string.Equals(args[ii], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[ii + 1];
                }
            }
            return null;
        }

        private static bool HasFlag(string[] args, string name)
        {
            return args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal enum VisualInspectionAgentMode
    {
        Scripted,

        LiveAI,

        Human
    }
}
