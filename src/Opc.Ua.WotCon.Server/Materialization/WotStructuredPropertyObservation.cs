/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Composes structured observations from the latest value of every mapped field.
    /// </summary>
    internal sealed class WotStructuredPropertyObservation : IAsyncDisposable
    {
        private WotStructuredPropertyObservation(
            WotStructuredGroupResolution resolution,
            ISystemContext context,
            NodeId targetNodeId,
            Action<WotNotification> notification)
        {
            m_resolution = resolution;
            m_context = context;
            m_targetNodeId = targetNodeId;
            m_notification = notification;
            m_logger = context.Telemetry.CreateLogger<WotStructuredPropertyObservation>();
            m_values = new (WotFieldPathPlan Plan, WotReadResult Result)[resolution.ReadFields.Count];
            for (int i = 0; i < m_values.Length; i++)
            {
                m_values[i] = (resolution.ReadFields[i].Plan, new WotReadResult(
                    StatusCodes.BadWaitingForInitialData,
                    DataValue.FromStatusCode(StatusCodes.BadWaitingForInitialData)));
            }
        }

        public static async ValueTask<IAsyncDisposable> StartAsync(
            WotStructuredGroupState state,
            ISystemContext context,
            Action<WotNotification> notification,
            CancellationToken cancellationToken)
        {
            WotStructuredGroupResolution resolution = state.EnsureResolved();
            if (!resolution.Success)
            {
                throw new ServiceResultException(resolution.Error);
            }
            var observation = new WotStructuredPropertyObservation(
                resolution, context, state.TargetNodeId, notification);
            try
            {
                for (int i = 0; i < resolution.ReadFields.Count; i++)
                {
                    int fieldIndex = i;
                    IWotBindingChannel channel = await resolution.ReadFields[i].Slot.GetAsync(cancellationToken)
                        .ConfigureAwait(false);
                    IWotSubscription subscription = await channel.ObserveAsync(
                        value => observation.OnNotification(fieldIndex, value), cancellationToken)
                        .ConfigureAwait(false);
                    observation.m_subscriptions.Add(subscription);
                }
            }
            catch (Exception startupError) when (startupError is not OutOfMemoryException)
            {
                try
                {
                    await observation.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupError) when (cleanupError is not OutOfMemoryException)
                {
                    throw new AggregateException("Structured observation startup and cleanup failed.",
                        startupError, cleanupError);
                }
                throw;
            }
            return observation;
        }

        public async ValueTask DisposeAsync()
        {
            lock (m_sync)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                Array.Clear(m_values, 0, m_values.Length);
            }
            List<Exception>? errors = null;
            foreach (IWotSubscription subscription in m_subscriptions)
            {
                try
                {
                    await subscription.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    (errors ??= []).Add(exception);
                }
            }
            m_subscriptions.Clear();
            if (errors is { Count: > 0 })
            {
                throw new AggregateException("One or more structured property observations failed to dispose.", errors);
            }
        }

        private void OnNotification(int fieldIndex, WotNotification notification)
        {
            lock (m_sync)
            {
                if (m_disposed)
                {
                    return;
                }
                WotFieldPathPlan plan = m_values[fieldIndex].Plan;
                DataValue value;
                try
                {
                    value = WotObservedPropertySource.TranslateNotification(notification, m_context);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    m_logger.FieldObservationFailed(exception, m_targetNodeId, plan.LeafFieldName);
                    value = notification.Value.WithWrappedValue(Variant.Null).WithStatus(
                        exception is ServiceResultException serviceException
                            ? serviceException.StatusCode
                            : StatusCodes.BadDecodingError);
                }
                m_values[fieldIndex] = (plan, new WotReadResult(value.StatusCode, value.Copy()));
                DataValue composed;
                try
                {
                    composed = Compose();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    m_logger.FieldObservationFailed(exception, m_targetNodeId, plan.LeafFieldName);
                    composed = value.WithWrappedValue(Variant.Null).WithStatus(
                        exception is ServiceResultException serviceException
                            ? serviceException.StatusCode
                            : StatusCodes.BadTypeMismatch);
                }
                m_notification(new WotNotification(composed).WithContext(m_context.AsMessageContext()));
            }
        }

        private DataValue Compose()
        {
            (StatusCode status, DateTimeUtc timestamp) = WotProjectionBindingRuntime.AggregateFieldMetadata(m_values);
            if (StatusCode.IsBad(status))
            {
                return new DataValue(Variant.Null, status, timestamp);
            }
            IEncodeable encodeable = m_resolution.RootType!.CreateInstance();
            if (encodeable is not IStructure root)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            foreach ((WotFieldPathPlan plan, WotReadResult result) in m_values)
            {
                IStructure parent = WotStructuredFieldNavigator.CreateOrGetChild(root, plan.IntermediateSegments);
                parent[plan.LeafFieldName] = result.Value.WrappedValue;
            }
            return new DataValue(new Variant(new ExtensionObject(encodeable)), status, timestamp);
        }

        private readonly WotStructuredGroupResolution m_resolution;
        private readonly ISystemContext m_context;
        private readonly NodeId m_targetNodeId;
        private readonly Action<WotNotification> m_notification;
        private readonly ILogger m_logger;
        private readonly (WotFieldPathPlan Plan, WotReadResult Result)[] m_values;
        private readonly List<IWotSubscription> m_subscriptions = [];
        private readonly Lock m_sync = new();
        private bool m_disposed;
    }

    internal static partial class WotStructuredPropertyObservationLog
    {
        [LoggerMessage(
            EventId = WotConServerEventIds.WotStructuredPropertyObservation,
            Level = LogLevel.Error,
            Message = "WoT property observation failed for local Node {NodeId}, field {Field}.")]
        public static partial void FieldObservationFailed(
            this ILogger logger, Exception exception, NodeId nodeId, string field);
    }
}
