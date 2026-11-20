/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// An annotation and the source timestamp of the value it annotates.
    /// </summary>
    /// <param name="SourceTimestamp">The source timestamp of the annotated historical value.</param>
    /// <param name="Annotation">The payload, whose AnnotationTime completes the annotation's identity.</param>
    /// <remarks>A default instance is an invalid update placeholder, not a stored annotation.</remarks>
    public readonly record struct HistorianAnnotation(
        DateTimeUtc SourceTimestamp,
        Annotation Annotation);
}
