// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace Opc.Ua.OpenUsd.Scene
{
    /// <summary>
    /// A materialized USD relationship — an ordered set of target paths
    /// (draft OPC UA — OpenUSD Scene Materialization §5.5).
    /// </summary>
    public sealed class UsdRelationship
    {
        /// <summary>
        /// Creates a relationship.
        /// </summary>
        /// <param name="name">The relationship name (for example <c>material:binding</c>).</param>
        public UsdRelationship(string name)
        {
            Name = name ?? string.Empty;
        }

        /// <summary>
        /// The relationship name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The ordered target SdfPath strings. Materialized both as the <c>TargetPaths</c>
        /// property (for fidelity when a target lies outside the materialized subtree) and,
        /// for each resolved target, as a <c>UsdRelationshipTarget</c> reference.
        /// </summary>
        public IList<string> Targets { get; } = new List<string>();

        /// <summary>
        /// Whether the relationship is a custom (non-schema) property.
        /// </summary>
        public bool Custom { get; set; }
    }
}
