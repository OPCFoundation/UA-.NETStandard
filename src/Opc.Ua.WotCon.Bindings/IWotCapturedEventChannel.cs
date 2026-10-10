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
