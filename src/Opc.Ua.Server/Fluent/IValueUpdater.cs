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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Runtime handle to a fluent-configured variable that survives the
    /// sealing of the <see cref="INodeManagerBuilder"/>. Obtained during
    /// <c>Configure</c> via
    /// <see cref="RuntimeValueBuilderExtensions.Bind{TValue}(IVariableBuilder{TValue}, out IValueUpdater{TValue})"/>,
    /// it lets the manager push value changes after start-up so both the
    /// Attribute (Read) service <b>and</b> subscribed MonitoredItems observe
    /// the update.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The old-style recipe — mutate <c>Node.Value</c> and call
    /// <c>Node.ClearChangeMasks(context, ...)</c> — is unavailable through
    /// the fluent surface because the builder is sealed once
    /// <c>Configure</c> returns. <see cref="SetValue(TValue)"/> performs the
    /// value assignment, timestamp/status update, and change-mask flush in a
    /// single serialized call so subscriptions see every transition.
    /// </para>
    /// <para>
    /// A single instance is safe to call from any thread; concurrent
    /// <see cref="SetValue(TValue)"/> calls are serialized internally.
    /// </para>
    /// </remarks>
    /// <typeparam name="TValue">
    /// CLR type carried by the variable's <c>Value</c> attribute.
    /// </typeparam>
    public interface IValueUpdater<in TValue>
    {
        /// <summary>
        /// Sets the variable value with a <see cref="StatusCodes.Good"/>
        /// status and the current UTC time as the source timestamp, then
        /// notifies subscribed MonitoredItems.
        /// </summary>
        /// <param name="value">The new value.</param>
        void SetValue(TValue value);

        /// <summary>
        /// Sets the variable value with the supplied status code and the
        /// current UTC time as the source timestamp, then notifies
        /// subscribed MonitoredItems.
        /// </summary>
        /// <param name="value">The new value.</param>
        /// <param name="statusCode">The status code to publish.</param>
        void SetValue(TValue value, StatusCode statusCode);

        /// <summary>
        /// Sets the variable value with the supplied status code and source
        /// timestamp, then notifies subscribed MonitoredItems.
        /// </summary>
        /// <param name="value">The new value.</param>
        /// <param name="statusCode">The status code to publish.</param>
        /// <param name="sourceTimestamp">The source timestamp to publish.</param>
        void SetValue(TValue value, StatusCode statusCode, DateTime sourceTimestamp);

        /// <summary>
        /// Flushes a value change notification to subscribed MonitoredItems
        /// without changing the stored value. Use when the underlying value
        /// object was mutated in place.
        /// </summary>
        void NotifyChange();
    }
}
