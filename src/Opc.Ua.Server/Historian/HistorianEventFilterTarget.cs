/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// <see cref="IFilterTarget"/> adapter that lets the framework's
    /// <see cref="FilterEvaluator"/> evaluate an
    /// <see cref="EventFilter.WhereClause"/> against a
    /// <see cref="HistorianEventRecord"/>'s flat field dictionary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IsTypeOf"/> uses the supplied <see cref="IFilterContext.TypeTree"/>
    /// for subtype resolution; an exact match on
    /// <see cref="HistorianEventRecord.EventType"/> always succeeds.
    /// When the type tree is unavailable subtype queries can only match
    /// exactly — that fallback emits a one-shot warning so operators see
    /// the misconfiguration; the read itself degrades safely.
    /// </para>
    /// <para>
    /// <see cref="GetAttributeValue"/> resolves the complete operand identity
    /// and applies the requested index range before returning a value.
    /// </para>
    /// </remarks>
    internal sealed class HistorianEventFilterTarget : IFilterTarget
    {
        public HistorianEventFilterTarget(HistorianEventRecord record)
        {
            m_record = record;
        }

        /// <inheritdoc/>
        public bool IsTypeOf(IFilterContext context, NodeId typeDefinitionId)
        {
            if (typeDefinitionId.IsNull)
            {
                return true;
            }
            if (m_record.EventType == typeDefinitionId)
            {
                return true;
            }
            if (context?.TypeTree != null)
            {
                return context.TypeTree.IsTypeOf(m_record.EventType, typeDefinitionId);
            }

            // TypeTree unavailable — log once so operators see the
            // misconfiguration; degrade safely (return false rather than
            // crashing the read or matching everything).
            if (Interlocked.CompareExchange(ref s_typeTreeWarningEmitted, 1, 0) == 0)
            {
                ILogger? logger = context?.Telemetry?.CreateLogger(nameof(HistorianEventFilterTarget));
                logger?.HistorianEventWhereClauseSubtypeQueryAgainstRequestedType(typeDefinitionId);
            }
            return false;
        }

        /// <inheritdoc/>
        public Variant GetAttributeValue(
            IFilterContext context,
            NodeId typeDefinitionId,
            ArrayOf<QualifiedName> relativePath,
            uint attributeId,
            NumericRange indexRange)
        {
            _ = context;
            if (relativePath.Count == 0)
            {
                if (attributeId == Attributes.NodeId)
                {
                    return new Variant(m_record.EventType);
                }
                return default;
            }

            var key = new HistorianEventFieldKey(
                typeDefinitionId,
                relativePath,
                attributeId,
                null);
            if (!m_record.TryGetQualifiedField(key, out Variant value) &&
                !TryResolveCompatibleField(
                    context,
                    typeDefinitionId,
                    relativePath,
                    attributeId,
                    out value))
            {
                if (m_record.QualifiedFields.Count != 0)
                {
                    return default;
                }
                string legacyKey = HistorianEventFieldKey.BuildPath(relativePath);
                if (!m_record.TryGetField(legacyKey, out value))
                {
                    return default;
                }
            }
            if (!indexRange.IsNull)
            {
                StatusCode status = indexRange.ApplyRange(ref value);
                if (StatusCode.IsBad(status))
                {
                    return default;
                }
            }
            return value;
        }

        private bool TryResolveCompatibleField(
            IFilterContext context,
            NodeId requestedType,
            ArrayOf<QualifiedName> requestedPath,
            uint requestedAttribute,
            out Variant value)
        {
            bool found = false;
            value = default;
            foreach (KeyValuePair<HistorianEventFieldKey, Variant> field in m_record.QualifiedFields)
            {
                HistorianEventFieldKey candidate = field.Key;
                if (candidate.AttributeId != requestedAttribute ||
                    !PathsEqual(candidate.BrowsePath, requestedPath) ||
                    (!candidate.TypeDefinitionId.IsNull &&
                        candidate.TypeDefinitionId != requestedType &&
                        (context?.TypeTree == null ||
                            !context.TypeTree.IsTypeOf(
                                m_record.EventType,
                                candidate.TypeDefinitionId))) ||
                    (!requestedType.IsNull &&
                        requestedType != candidate.TypeDefinitionId &&
                        (context?.TypeTree == null ||
                            !context.TypeTree.IsTypeOf(
                                m_record.EventType,
                                requestedType))))
                {
                    continue;
                }
                if (found)
                {
                    value = default;
                    return false;
                }
                value = field.Value;
                found = true;
            }
            return found;
        }

        private static bool PathsEqual(
            ArrayOf<QualifiedName> left,
            ArrayOf<QualifiedName> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }
            for (int i = 0; i < left.Count; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }
            return true;
        }

        private readonly HistorianEventRecord m_record;
        private static int s_typeTreeWarningEmitted;
    }

    /// <summary>
    /// Source-generated log messages for HistorianEventFilterTarget.
    /// </summary>
    internal static partial class HistorianEventFilterTargetLog
    {
        [LoggerMessage(EventId = ServerEventIds.HistorianEventFilterTarget + 0, Level = LogLevel.Warning,
            Message = "Historian event WhereClause subtype query against {RequestedType} could not be " +
                "resolved: IFilterContext.TypeTree is null. Event-type subtype matching is degraded " +
                "(exact match only) for this and subsequent reads in the current process.")]
        public static partial void HistorianEventWhereClauseSubtypeQueryAgainstRequestedType(
            this ILogger logger,
            NodeId requestedType);
    }
}
