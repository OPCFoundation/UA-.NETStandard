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

using System.Text;
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
    /// <see cref="GetAttributeValue"/> ignores the index-range argument
    /// (returns the full array). Index-range support can be added when
    /// providers need it.
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
            _ = typeDefinitionId;
            _ = indexRange;

            if (relativePath.Count == 0)
            {
                if (attributeId == Attributes.NodeId)
                {
                    return new Variant(m_record.EventType);
                }
                return default;
            }

            string key = BuildKey(relativePath);
            return m_record.Fields.TryGetValue(key, out Variant value) ? value : default;
        }

        private static string BuildKey(ArrayOf<QualifiedName> relativePath)
        {
            if (relativePath.Count == 1)
            {
                return relativePath[0].Name ?? string.Empty;
            }
            var sb = new StringBuilder();
            for (int i = 0; i < relativePath.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('/');
                }
                sb.Append(relativePath[i].Name);
            }
            return sb.ToString();
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
