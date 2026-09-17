/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// An annotation and the source timestamp of the value it annotates.
    /// </summary>
    public readonly record struct HistorianAnnotation(
        DateTimeUtc SourceTimestamp,
        Annotation Annotation);
}
