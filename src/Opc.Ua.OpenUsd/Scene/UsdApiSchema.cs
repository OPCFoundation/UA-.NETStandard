// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.OpenUsd.Scene
{
    /// <summary>
    /// An applied USD API schema, materialized as an AddIn under a prim's
    /// <c>AppliedSchemas</c> folder (draft OPC UA — OpenUSD Scene Materialization §5.6, §8.2).
    /// An unknown API schema is never dropped: it degrades to a generic
    /// <c>UsdApiSchemaType</c> AddIn carrying <see cref="SchemaName"/> (§8.4).
    /// </summary>
    public sealed class UsdApiSchema
    {
        private static readonly char[] s_colon = [':'];

        /// <summary>
        /// Creates an applied API schema.
        /// </summary>
        /// <param name="schemaName">The schema token as authored, for example
        /// <c>CollectionAPI:lights</c> or <c>CesiumGlobeAnchorAPI</c>.</param>
        public UsdApiSchema(string schemaName)
        {
            SchemaName = schemaName ?? string.Empty;
            string[] parts = SchemaName.Split(s_colon, 2);
            FamilyName = parts[0];
            InstanceName = parts.Length > 1 ? parts[1] : string.Empty;
        }

        /// <summary>
        /// The applied schema token exactly as authored.
        /// </summary>
        public string SchemaName { get; }

        /// <summary>
        /// The instance name of a multiple-apply schema — the portion after the colon
        /// (for example <c>lights</c> in <c>CollectionAPI:lights</c>), or an empty string
        /// for a single-apply schema.
        /// </summary>
        public string InstanceName { get; }

        /// <summary>
        /// The schema family name — the portion before the colon (for example
        /// <c>CollectionAPI</c>), which selects the materialized AddIn ObjectType.
        /// </summary>
        public string FamilyName { get; }

        /// <summary>
        /// The expansion rule of a collection-style schema, when authored.
        /// </summary>
        public string ExpansionRule { get; set; } = string.Empty;
    }
}
