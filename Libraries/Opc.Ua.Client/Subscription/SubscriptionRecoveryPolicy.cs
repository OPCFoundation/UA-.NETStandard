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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Controls how the client reacts to a server-emitted
    /// <c>Good_SubscriptionTransferred</c> status change notification
    /// (OPC UA Part 4 §5.14.7 / §7.25.4).
    /// <para>
    /// Per the specification the notification is sent to the
    /// <i>old</i> Session when its Subscription was transferred away
    /// to another Session via the <c>TransferSubscriptions</c>
    /// Service. Some servers (e.g. Kepware after a server
    /// reinitialisation) have been observed to emit the notification
    /// against a Subscription that the client only just created on
    /// the new Session, which under the default behaviour leaves the
    /// Subscription dark with no further notifications dispatched.
    /// </para>
    /// </summary>
    public enum SubscriptionRecoveryPolicy
    {
        /// <summary>
        /// Spec-strict, backwards-compatible behaviour: raise the
        /// <c>Transferred</c> publish state flag so the application
        /// can react, but do not attempt to recreate the
        /// Subscription. The Subscription is treated as transferred
        /// away from this Session and stops processing
        /// notifications on the client side.
        /// </summary>
        ReportOnly = 0,

        /// <summary>
        /// If the client did not initiate a TransferSubscriptions
        /// request for this Subscription, treat the notification as
        /// the server having invalidated the Subscription on the
        /// current Session and recreate it in place using the
        /// existing options. Resolves the symptom of a "stuck"
        /// Subscription after server-initiated quirks but does not
        /// preserve the server-side retransmission queue or
        /// triggering relationships that were tied to the
        /// invalidated Subscription identifier.
        /// </summary>
        RecreateOnUnsolicitedTransfer = 1
    }
}
