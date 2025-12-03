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
using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// Provides context information to used when searching the address space.
    /// </summary>
    public class FilterContext : IFilterContext
    {
        /// <summary>
        /// Initializes the context.
        /// </summary>
        [Obsolete("Use constructor with ITelemetryContext")]
        public FilterContext(
            NamespaceTable namespaceUris,
            ITypeTable typeTree,
            IOperationContext context)
            : this(namespaceUris, typeTree, context, null)
        {
        }

        /// <summary>
        /// Initializes the context.
        /// </summary>
        [Obsolete("Use constructor with ITelemetryContext")]
        public FilterContext(
            NamespaceTable namespaceUris,
            ITypeTable typeTree)
            : this(namespaceUris, typeTree, (ITelemetryContext)null)
        {
        }

        /// <summary>
        /// Initializes the context.
        /// </summary>
        [Obsolete("Use constructor with ITelemetryContext")]
        public FilterContext(
            NamespaceTable namespaceUris,
            ITypeTable typeTree,
            IList<string> preferredLocales)
            : this(namespaceUris, typeTree, preferredLocales, null)
        {
        }

        /// <summary>
        /// Initializes the context.
        /// </summary>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <param name="typeTree">The type tree.</param>
        /// <param name="context">The context.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public FilterContext(
            NamespaceTable namespaceUris,
            ITypeTable typeTree,
            IOperationContext context,
            ITelemetryContext telemetry)
        {
            NamespaceUris = namespaceUris ?? throw new ArgumentNullException(nameof(namespaceUris));
            TypeTree = typeTree ?? throw new ArgumentNullException(nameof(typeTree));
            Telemetry = telemetry;
            m_context = context;
        }

        /// <summary>
        /// Initializes the context.
        /// </summary>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <param name="typeTree">The type tree.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public FilterContext(
            NamespaceTable namespaceUris,
            ITypeTable typeTree,
            ITelemetryContext telemetry)
            : this(namespaceUris, typeTree, (IList<string>)null, telemetry)
        {
        }

        /// <summary>
        /// Initializes the context.
        /// </summary>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <param name="typeTree">The type tree.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public FilterContext(
            NamespaceTable namespaceUris,
            ITypeTable typeTree,
            IList<string> preferredLocales,
            ITelemetryContext telemetry)
        {
            NamespaceUris = namespaceUris ?? throw new ArgumentNullException(nameof(namespaceUris));
            TypeTree = typeTree ?? throw new ArgumentNullException(nameof(typeTree));
            Telemetry = telemetry;
            m_context = null;
            m_preferredLocales = preferredLocales;
        }

        /// <summary>
        /// The namespace table to use when evaluating filters.
        /// </summary>
        /// <value>The namespace URIs.</value>
        public NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// The type tree to use when evaluating filters.
        /// </summary>
        /// <value>The type tree.</value>
        public ITypeTable TypeTree { get; }

        /// <summary>
        /// The identifier for the session (null if multiple sessions are associated with the operation).
        /// </summary>
        /// <value>The session identifier.</value>
        public NodeId SessionId
        {
            get
            {
                if (m_context is ISessionOperationContext session)
                {
                    return session.SessionId;
                }

                return null;
            }
        }

        /// <summary>
        /// The identity of the user.
        /// </summary>
        /// <value>The user identity.</value>
        public IUserIdentity UserIdentity
        {
            get
            {
                if (m_context is ISessionOperationContext session)
                {
                    return session.UserIdentity;
                }

                return null;
            }
        }

        /// <summary>
        /// The locales to use if available.
        /// </summary>
        /// <value>The preferred locales.</value>
        public IList<string> PreferredLocales
        {
            get
            {
                if (m_context != null)
                {
                    return m_context.PreferredLocales;
                }

                return m_preferredLocales;
            }
        }

        /// <summary>
        /// The mask to use when collecting any diagnostic information.
        /// </summary>
        /// <value>The diagnostics mask.</value>
        public DiagnosticsMasks DiagnosticsMask
        {
            get
            {
                if (m_context != null)
                {
                    return m_context.DiagnosticsMask;
                }

                return DiagnosticsMasks.SymbolicId;
            }
        }

        /// <summary>
        /// The table of strings which is used to store diagnostic string data.
        /// </summary>
        /// <value>The string table.</value>
        public StringTable StringTable
        {
            get
            {
                if (m_context != null)
                {
                    return m_context.StringTable;
                }

                return null;
            }
        }

        /// <summary>
        /// When the operation times out.
        /// </summary>
        /// <value>The operation deadline.</value>
        public DateTime OperationDeadline
        {
            get
            {
                if (m_context != null)
                {
                    return m_context.OperationDeadline;
                }

                return DateTime.MaxValue;
            }
        }

        /// <summary>
        /// The current status of the operation (bad if the operation has been aborted).
        /// </summary>
        /// <value>The operation status.</value>
        public StatusCode OperationStatus
        {
            get
            {
                if (m_context != null)
                {
                    return m_context.OperationStatus;
                }

                return StatusCodes.Good;
            }
        }

        /// <summary>
        /// The audit identifier associated with the operation.
        /// </summary>
        /// <value>The audit entry identifier.</value>
        public string AuditEntryId
        {
            get
            {
                if (m_context != null)
                {
                    return m_context.AuditEntryId;
                }

                return null;
            }
        }

        /// <summary>
        /// Telemetry context for the filter
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        private readonly IOperationContext m_context;
        private readonly IList<string> m_preferredLocales;
    }
}
