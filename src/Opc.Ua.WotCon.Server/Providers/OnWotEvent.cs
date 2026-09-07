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

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Delegate invoked by an <see cref="IWotAssetProvider"/> when an asset
    /// emits a subscribed WoT event.
    /// </summary>
    /// <param name="tag">The event affordance that fired.</param>
    /// <param name="fields">
    /// The event payload, one value per <see cref="WotEventTag.Fields"/>
    /// entry and in the same order. A provider that has no value for a field
    /// supplies <see cref="Variant.Null"/>.
    /// </param>
    /// <param name="message">
    /// The human-readable message published as
    /// <c>BaseEventType.Message</c>. When null the event name is used.
    /// </param>
    /// <param name="severity">
    /// The OPC 10000-5 <c>BaseEventType.Severity</c> (1..1000). When null
    /// the server fallback in <see cref="WotEventTag.Severity"/> is used.
    /// </param>
    /// <param name="timestamp">
    /// The time the asset reported the event, published as
    /// <c>BaseEventType.Time</c>.
    /// </param>
    public delegate void OnWotEvent(
        WotEventTag tag,
        IReadOnlyList<Variant> fields,
        LocalizedText? message,
        ushort? severity,
        DateTime timestamp);
}
