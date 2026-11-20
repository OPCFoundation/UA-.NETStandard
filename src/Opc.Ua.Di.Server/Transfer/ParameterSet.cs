/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;

namespace Opc.Ua.Di.Server.Transfer
{
    /// <summary>
    /// A single parameter value carried by a <see cref="ParameterSet"/>.
    /// The <see cref="NodePath"/> is the chain of
    /// <see cref="QualifiedName"/>s that locate the parameter relative
    /// to the topology element (typically a device).
    /// </summary>
    /// <param name="NodePath">
    /// Browse-name chain identifying the parameter under the topology
    /// element (e.g. <c>["ParameterSet", "DeviceManual"]</c>).
    /// </param>
    /// <param name="Value">The parameter value.</param>
    /// <param name="StatusCode">
    /// Per-parameter status. <see cref="StatusCodes.Good"/>
    /// for successful transfers; per-parameter bad codes are surfaced
    /// to the client via <c>FetchTransferResultData</c>.
    /// </param>
    public sealed record ParameterEntry(
        QualifiedName[] NodePath,
        Variant Value,
        StatusCode StatusCode);

    /// <summary>
    /// An ordered set of parameters representing one snapshot of a
    /// topology element's configurable state. Created by
    /// <c>ITransferService.ExportAsync</c> and consumed by
    /// <c>ITransferService.ImportAsync</c>.
    /// </summary>
    public sealed class ParameterSet
    {
        /// <summary>
        /// Creates an empty parameter set for
        /// <paramref name="elementId"/>.
        /// </summary>
        public ParameterSet(NodeId elementId)
        {
            if (elementId.IsNull)
            {
                throw new System.ArgumentNullException(nameof(elementId));
            }
            ElementId = elementId;
            Entries = [];
        }

        /// <summary>
        /// Creates a parameter set with the supplied entries.
        /// </summary>
        public ParameterSet(NodeId elementId, IList<ParameterEntry> entries)
        {
            if (elementId.IsNull)
            {
                throw new System.ArgumentNullException(nameof(elementId));
            }
            ElementId = elementId;
            Entries = entries ?? throw new System.ArgumentNullException(nameof(entries));
        }

        /// <summary>
        /// The topology element this set belongs to.
        /// </summary>
        public NodeId ElementId { get; }

        /// <summary>
        /// The parameter entries in this set, in insertion order.
        /// </summary>
        public IList<ParameterEntry> Entries { get; }
    }
}
