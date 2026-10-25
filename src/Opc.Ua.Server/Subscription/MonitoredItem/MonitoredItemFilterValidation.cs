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

using System;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Shares source-metadata filter validation between bound and unresolved monitored items.
    /// </summary>
    internal static class MonitoredItemFilterValidation
    {
        /// <summary>
        /// Validates a filter and constructs an aggregate filter for subsequent source-specific revision.
        /// </summary>
        internal static AsyncCustomNodeManager.ValidateMonitoringFilterResult Validate(
            IServerInternal server,
            ServerSystemContext context,
            NodeState? metadata,
            uint attributeId,
            ExtensionObject filter)
        {
            var result = new AsyncCustomNodeManager.ValidateMonitoringFilterResult();
            if (filter.IsNull)
            {
                result.StatusCode = StatusCodes.Good;
                return result;
            }

            if (ExtensionObject.ToEncodeable(filter) is not DataChangeFilter deadband)
            {
                if (ExtensionObject.ToEncodeable(filter) is not AggregateFilter aggregate ||
                    attributeId != Attributes.Value)
                {
                    result.StatusCode = StatusCodes.BadFilterNotAllowed;
                    return result;
                }
                if (!server.AggregateManager.IsSupported(aggregate.AggregateType))
                {
                    result.StatusCode = StatusCodes.BadAggregateNotSupported;
                    return result;
                }
                result.FilterToUse = new ServerAggregateFilter
                {
                    AggregateType = aggregate.AggregateType,
                    StartTime = aggregate.StartTime,
                    ProcessingInterval = aggregate.ProcessingInterval,
                    AggregateConfiguration = aggregate.AggregateConfiguration,
                    Stepped = false,
                    PrimeInitialValue = true
                };
                result.StatusCode = StatusCodes.Good;
                return result;
            }

            if (attributeId != Attributes.Value || metadata is not BaseVariableState variable)
            {
                result.StatusCode = StatusCodes.BadFilterNotAllowed;
                return result;
            }
            if (deadband.DeadbandType == (uint)DeadbandType.None)
            {
                result.FilterToUse = deadband;
                result.StatusCode = StatusCodes.Good;
                return result;
            }
            if (!server.TypeTree.IsTypeOf(variable.DataType, DataTypeIds.Number))
            {
                result.StatusCode = StatusCodes.BadFilterNotAllowed;
                return result;
            }
            if (deadband.DeadbandType == (uint)DeadbandType.Absolute)
            {
                result.FilterToUse = deadband;
                result.StatusCode = StatusCodes.Good;
                return result;
            }
            if (deadband.DeadbandType == (uint)DeadbandType.Percent)
            {
                if (metadata.FindChild(context, QualifiedName.From(BrowseNames.EURange)) is not
                    PropertyState property ||
                    !property.Value.TryGetStructure<Range>(out Range? range))
                {
                    result.StatusCode = StatusCodes.BadDeadbandFilterInvalid;
                    return result;
                }
                result.FilterToUse = deadband;
                result.Range = range;
                result.StatusCode = StatusCodes.Good;
                return result;
            }

            result.StatusCode = StatusCodes.BadFilterNotAllowed;
            return result;
        }

        /// <summary>
        /// Revises an aggregate without a source-specific historian provider.
        /// </summary>
        internal static void ReviseDefaultAggregate(
            IServerInternal server,
            double samplingInterval,
            uint queueSize,
            ServerAggregateFilter filter)
        {
            filter.HistorianProvider = null;
            filter.HistorianCapabilities = null;
            filter.HistorianKeySelector = TimestampStructuredDataKeySelector.Instance;
            filter.ReviseProcessingInterval(samplingInterval, server.AggregateManager.MinimumProcessingInterval);
            DateTimeUtc currentTime = ((server as ITimeProviderProvider)?.TimeProvider ??
                TimeProvider.System).GetUtcNow().UtcDateTime;
            filter.ReviseStartTime(currentTime, queueSize);
            if (filter.AggregateConfiguration.UseServerCapabilitiesDefaults)
            {
                filter.AggregateConfiguration = server.AggregateManager.GetDefaultConfiguration(default);
            }
        }

        /// <summary>
        /// Captures the successfully revised aggregate settings for the client.
        /// </summary>
        internal static AggregateFilterResult CreateAggregateResult(ServerAggregateFilter filter)
        {
            return new AggregateFilterResult
            {
                RevisedProcessingInterval = filter.ProcessingInterval,
                RevisedStartTime = filter.StartTime,
                RevisedAggregateConfiguration = filter.AggregateConfiguration
            };
        }
    }
}
