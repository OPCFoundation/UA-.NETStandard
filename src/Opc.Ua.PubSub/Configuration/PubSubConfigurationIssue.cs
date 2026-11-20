/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// A single issue raised by the
    /// <see cref="PubSubConfigurationValidator"/> or by the snapshot
    /// builder. Issues carry a stable <see cref="Code"/> so callers can
    /// suppress or surface them programmatically, a path that names the
    /// offending element inside the configuration tree, and (where
    /// applicable) the OPC UA Part 14 clause that defines the rule.
    /// </summary>
    public sealed record PubSubConfigurationIssue
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubConfigurationIssue"/>.
        /// </summary>
        /// <param name="severity">Severity bucket.</param>
        /// <param name="code">
        /// Stable, machine-readable identifier (e.g. PSC0001).
        /// </param>
        /// <param name="message">Human-readable diagnostic.</param>
        /// <param name="path">
        /// Dotted path that locates the offending element in the
        /// configuration tree (e.g. Connections[0].WriterGroups[1]).
        /// </param>
        /// <param name="specClause">
        /// Optional Part 14 clause reference (e.g. "6.2.5.4").
        /// </param>
        public PubSubConfigurationIssue(
            PubSubConfigurationIssueSeverity severity,
            string code,
            string message,
            string path,
            string? specClause = null)
        {
            if (code is null)
            {
                throw new ArgumentNullException(nameof(code));
            }
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }
            if (path is null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            Severity = severity;
            Code = code;
            Message = message;
            Path = path;
            SpecClause = specClause;
        }

        /// <summary>
        /// Severity bucket.
        /// </summary>
        public PubSubConfigurationIssueSeverity Severity { get; init; }

        /// <summary>
        /// Stable, machine-readable identifier.
        /// </summary>
        public string Code { get; init; }

        /// <summary>
        /// Human-readable diagnostic.
        /// </summary>
        public string Message { get; init; }

        /// <summary>
        /// Dotted path that locates the offending element in the
        /// configuration tree.
        /// </summary>
        public string Path { get; init; }

        /// <summary>
        /// Optional Part 14 clause reference.
        /// </summary>
        public string? SpecClause { get; init; }
    }
}
