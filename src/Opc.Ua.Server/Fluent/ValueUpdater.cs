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
    /// Default <see cref="IValueUpdater{TValue}"/> implementation. Closes
    /// over the resolved <see cref="BaseVariableState"/> and the manager's
    /// <see cref="ISystemContext"/> captured at <c>Configure</c> time, so it
    /// remains usable after the builder is sealed.
    /// </summary>
    /// <typeparam name="TValue">
    /// CLR type carried by the variable's <c>Value</c> attribute.
    /// </typeparam>
    internal sealed class ValueUpdater<TValue> : IValueUpdater<TValue>
    {
        public ValueUpdater(BaseVariableState variable, ISystemContext context)
        {
            m_variable = variable ?? throw new ArgumentNullException(nameof(variable));
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public void SetValue(TValue value)
        {
            SetValue(value, StatusCodes.Good, DateTime.UtcNow);
        }

        /// <inheritdoc/>
        public void SetValue(TValue value, StatusCode statusCode)
        {
            SetValue(value, statusCode, DateTime.UtcNow);
        }

        /// <inheritdoc/>
        public void SetValue(TValue value, StatusCode statusCode, DateTime sourceTimestamp)
        {
            lock (m_lock)
            {
                m_variable.Value = FluentVariant.ToVariant(value);
                m_variable.StatusCode = statusCode;
                m_variable.Timestamp = sourceTimestamp;
                m_variable.ClearChangeMasks(m_context, includeChildren: false);
            }
        }

        /// <inheritdoc/>
        public void NotifyChange()
        {
            lock (m_lock)
            {
                // Explicitly flag a value change so the notification fires even
                // when the value object was mutated in place (no setter ran).
                m_variable.UpdateChangeMasks(NodeStateChangeMasks.Value);
                m_variable.ClearChangeMasks(m_context, includeChildren: false);
            }
        }

        private readonly BaseVariableState m_variable;
        private readonly ISystemContext m_context;
        private readonly System.Threading.Lock m_lock = new();
    }
}
