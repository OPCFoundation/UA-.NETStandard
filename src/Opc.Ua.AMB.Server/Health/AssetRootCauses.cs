// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.AMB.Server.Health
{
    /// <summary>
    /// The values of <c>PotentialRootCauses</c> OPC 10000-110 §9.4.2
    /// distinguishes.
    /// </summary>
    public static class AssetRootCauses
    {
        /// <summary>
        /// The text of the entry that says no root cause has been identified.
        /// </summary>
        public const string UnknownText = "unknown";

        /// <summary>
        /// Gets the list that says no potential root cause has been
        /// identified: one entry without a <c>RootCauseId</c>.
        /// </summary>
        public static ArrayOf<RootCauseDataType> Unknown =>
        [
            new RootCauseDataType { RootCauseId = NodeId.Null, RootCause = new LocalizedText(UnknownText) }
        ];

        /// <summary>
        /// Gets the list that says the alarm itself is the root cause: an
        /// empty one.
        /// </summary>
        public static ArrayOf<RootCauseDataType> Self => ArrayOf<RootCauseDataType>.Empty;

        /// <summary>
        /// Creates one potential root cause.
        /// </summary>
        /// <param name="rootCauseId">
        /// The node that is the root cause, for example another node of the
        /// address space or a ConditionId; <see cref="NodeId.Null"/> when
        /// there is none.
        /// </param>
        /// <param name="rootCause">The description of the root cause.</param>
        /// <returns>The root cause.</returns>
        /// <exception cref="ArgumentException">
        /// Neither a node nor a description is given.
        /// </exception>
        public static RootCauseDataType Of(NodeId rootCauseId, LocalizedText rootCause)
        {
            if (rootCauseId.IsNull && rootCause.IsNullOrEmpty)
            {
                throw new ArgumentException("A root cause needs a node or a description.", nameof(rootCause));
            }
            return new RootCauseDataType { RootCauseId = rootCauseId, RootCause = rootCause };
        }

        /// <summary>
        /// Applies the rules of §9.4.2 to what an application passed: no list
        /// means the root cause is unknown; an empty list means the alarm
        /// itself is the root cause.
        /// </summary>
        /// <exception cref="ArgumentException">An entry is <c>null</c>.</exception>
        internal static ArrayOf<RootCauseDataType> Normalize(ArrayOf<RootCauseDataType> rootCauses)
        {
            if (rootCauses.IsNull)
            {
                return Unknown;
            }
            foreach (RootCauseDataType? rootCause in rootCauses)
            {
                if (rootCause == null)
                {
                    throw new ArgumentException("A potential root cause must not be null.", nameof(rootCauses));
                }
            }
            return rootCauses;
        }
    }
}
