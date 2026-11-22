// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// The acknowledgements the session's deferral policy selected for the next Publish
    /// request and those it retains for a later one.
    /// </summary>
    /// <remarks>
    /// The lists are the ones the legacy
    /// <see cref="ISession.PublishSequenceNumbersToAcknowledge"/> event mutates in place;
    /// they are not copied.
    /// </remarks>
    /// <param name="ToSend">The acknowledgements to send.</param>
    /// <param name="Deferred">The acknowledgements to retain for a later request.</param>
    internal readonly record struct AcknowledgementSelection(
        List<SubscriptionAcknowledgement> ToSend,
        List<SubscriptionAcknowledgement> Deferred);
}
