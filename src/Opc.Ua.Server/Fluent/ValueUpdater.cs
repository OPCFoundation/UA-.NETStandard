/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
