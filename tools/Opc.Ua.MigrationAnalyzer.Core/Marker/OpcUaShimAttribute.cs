// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Marks an API member as a 1.5.378 → 1.6 migration shim. Used by the
    /// <c>Opc.Ua.MigrationAnalyzer</c> analyzer to map calls that bind to a shim
    /// extension back to the underlying <c>UA00xx</c> diagnostic rule, so
    /// consumers get the same migration guidance whether they call the
    /// shim directly or the legacy API in source.
    /// </summary>
    /// <remarks>
    /// Apply this in addition to <see cref="ObsoleteAttribute"/>. The shim
    /// keeps the call compilable; the analyzer fires an <c>Info</c>
    /// diagnostic that points at the matching migration-guide section.
    /// </remarks>
    [AttributeUsage(
        AttributeTargets.Method |
        AttributeTargets.Property |
        AttributeTargets.Constructor |
        AttributeTargets.Class,
        AllowMultiple = false,
        Inherited = false)]
    public sealed class OpcUaShimAttribute : Attribute
    {
        /// <summary>
        /// The diagnostic rule identifier the shim corresponds to,
        /// e.g. <c>"UA0008"</c>.
        /// </summary>
        public string RuleId { get; }

        /// <summary>
        /// Creates a new <see cref="OpcUaShimAttribute"/>.
        /// </summary>
        public OpcUaShimAttribute(string ruleId)
        {
            RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
        }
    }
}
