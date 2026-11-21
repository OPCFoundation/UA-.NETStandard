// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// Optional source-binding capture for native projection. Required Core
    /// fields accompany, but do not extend, the public notification selection.
    /// </summary>
    public interface IWotCapturedEventChannel : IWotBindingChannel
    {
        /// <summary>
        /// Validates the selected event notifier and returns its current,
        /// authenticated source binding without creating a subscription.
        /// The channel retains ownership of the captured binding.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The channel cannot prove a current authenticated source or the
        /// selected target does not support event subscription.
        /// </exception>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        ValueTask<WotEventSource> CaptureEventSourceAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Subscribes with private BaseEventType capture and, when requested,
        /// common ConditionType fields including the empty-path ConditionId.
        /// The returned subscription retains the ordinary channel lifetime contract.
        /// </summary>
        /// <param name="captureConditionFields">Whether the projection requires ConditionType capture.</param>
        /// <param name="onEvent">Receives the unchanged public selection and its captured source facts.</param>
        /// <param name="cancellationToken">Cancels subscription creation.</param>
        /// <exception cref="ServiceResultException">
        /// The source cannot provide the requested captured binding or required fields.
        /// </exception>
        ValueTask<IWotSubscription> SubscribeCapturedEventAsync(
            bool captureConditionFields,
            Action<WotNotification> onEvent,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Subscribes with private fields required by the advertised Core event
        /// representation, without extending the public notification selection.
        /// </summary>
        /// <param name="coreEventType">
        /// BaseEventType, ConditionType, AcknowledgeableConditionType,
        /// AlarmConditionType, or LimitAlarmType.
        /// </param>
        /// <param name="onEvent">Receives the public selection and captured source facts.</param>
        /// <param name="cancellationToken">Cancels subscription creation.</param>
        /// <exception cref="ServiceResultException">
        /// The Core representation or its required source fields are unsupported.
        /// </exception>
        ValueTask<IWotSubscription> SubscribeCapturedEventAsync(
            NodeId coreEventType,
            Action<WotNotification> onEvent,
            CancellationToken cancellationToken = default);
    }
}
