/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// A sample returned from an <see cref="IHistorianModifiedProvider.ReadModifiedAsync"/>
    /// call: the underlying <see cref="DataValue"/> plus the
    /// <see cref="ModificationInfo"/> describing how/when/by-whom the
    /// sample was modified or deleted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Modified history (Part 11 §5.2.5) returns all replaced and deleted
    /// versions of samples in the requested time range plus their
    /// <see cref="ModificationInfo"/>. Providers must order results by
    /// <c>SourceTimestamp</c> ascending; samples for the same source
    /// timestamp must be ordered by <see cref="ModificationInfo.ModificationTime"/>
    /// ascending.
    /// </para>
    /// </remarks>
    public readonly record struct ModifiedDataValue(DataValue Value, ModificationInfo Info);
}
