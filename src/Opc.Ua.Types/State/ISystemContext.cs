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
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// An interface to an object that describes how access the system containing the data.
    /// </summary>
    public interface ISystemContext
    {
        /// <summary>
        /// An application defined handle for the system.
        /// </summary>
        /// <value>The system handle.</value>
        object? SystemHandle { get; }

        /// <summary>
        /// Returns the display name of the user associated
        /// with the context.
        /// </summary>
        /// <value>The id of the current user.</value>
        string? UserId { get; }

        /// <summary>
        /// The locales to use if available.
        /// </summary>
        /// <value>The preferred locales.</value>
        ArrayOf<string> PreferredLocales { get; }

        /// <summary>
        /// The audit log entry associated with the operation (null if not available).
        /// </summary>
        /// <value>The audit entry identifier.</value>
        string? AuditEntryId { get; }

        /// <summary>
        /// The table of namespace uris to use when accessing the system.
        /// </summary>
        /// <value>The namespace URIs.</value>
        NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// The table of server uris to use when accessing the system.
        /// </summary>
        /// <value>The server URIs.</value>
        StringTable ServerUris { get; }

        /// <summary>
        /// A table containing the types that are to be used when accessing the system.
        /// </summary>
        /// <value>The type table.</value>
        ITypeTable TypeTable { get; }

        /// <summary>
        /// A factory that can be used to create encodeable types.
        /// </summary>
        /// <value>The encodeable factory.</value>
        IEncodeableFactory EncodeableFactory { get; }

        /// <summary>
        /// A factory that can be used to create node ids.
        /// </summary>
        /// <value>
        /// The node identifiers factory, or <c>null</c> when the context
        /// suppresses NodeId assignment. Callers that assign NodeIds must
        /// check for <c>null</c>; see
        /// <see cref="NodeIdFactorySuppressedContext"/>, which a node copy
        /// uses so materialising its children does not consume identifiers
        /// the copy immediately overwrites.
        /// </value>
        INodeIdFactory? NodeIdFactory { get; }

        /// <summary>
        /// A factory that can be used to create encodeable types.
        /// </summary>
        /// <value>The encodeable factory.</value>
        NodeStateFactory NodeStateFactory { get; }

        /// <summary>
        /// Telemetry context for logging and tracing in the system
        /// </summary>
        ITelemetryContext Telemetry { get; }
    }

    /// <summary>
    /// An interface that can be used to create new node ids.
    /// </summary>
    public interface INodeIdFactory
    {
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        /// <returns>The new NodeId.</returns>
        NodeId New(ISystemContext context, NodeState node);
    }

    /// <summary>
    /// A context that knows the operation it was created for.
    /// <para>
    /// Implement this alongside <see cref="ISystemContext"/> so that a callback which receives
    /// the context can hand its operation to an API that has to know which operation invoked it.
    /// A context that wraps another context forwards the operation of the context it wraps.
    /// </para>
    /// </summary>
    public interface IOperationContextProvider
    {
        /// <summary>
        /// The operation the context was created for, or <c>null</c> when the context does not
        /// belong to an operation.
        /// </summary>
        IOperationContext? OperationContext { get; }
    }

    /// <summary>
    /// A generic implementation for ISystemContext interface.
    /// </summary>
    public class SystemContext : ISystemContext, IOperationContext, IOperationContextProvider
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SystemContext"/> class.
        /// </summary>
        public SystemContext(ITelemetryContext telemetry)
        {
            Telemetry = telemetry;
            NodeStateFactory = new NodeStateFactory();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SystemContext"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public SystemContext(IOperationContext context, ITelemetryContext telemetry)
        {
            Telemetry = telemetry;
            NodeStateFactory = new NodeStateFactory();
            OperationContext = context;
        }

        /// <inheritdoc/>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// An application defined handle for the system.
        /// </summary>
        /// <value>The system handle.</value>
        public object? SystemHandle { get; set; }

        /// <summary>
        /// Returns the display name of the user associated
        /// with the context.
        /// </summary>
        /// <value>The id of the current user.</value>
        public string? UserId { get; set; }

        /// <summary>
        /// The locales to use if available.
        /// </summary>
        /// <value>The preferred locales.</value>
        public ArrayOf<string> PreferredLocales
        {
            get
            {
                if (OperationContext != null)
                {
                    return OperationContext.PreferredLocales;
                }

                return m_preferredLocales;
            }
            set => m_preferredLocales = value;
        }

        /// <summary>
        /// The audit log entry associated with the operation (null if not available).
        /// </summary>
        /// <value>The audit entry identifier.</value>
        public string? AuditEntryId
        {
            get
            {
                if (OperationContext != null)
                {
                    return OperationContext.AuditEntryId;
                }

                return m_auditEntryId;
            }
            set => m_auditEntryId = value;
        }

        /// <summary>
        /// The table of namespace uris to use when accessing the system.
        /// </summary>
        /// <value>The namespace URIs.</value>
        public NamespaceTable NamespaceUris { get; set; } = null!;

        /// <summary>
        /// The table of server uris to use when accessing the system.
        /// </summary>
        /// <value>The server URIs.</value>
        public StringTable ServerUris { get; set; } = null!;

        /// <summary>
        /// A table containing the types that are to be used when accessing the system.
        /// </summary>
        /// <value>The type table.</value>
        public ITypeTable TypeTable { get; set; } = null!;

        /// <summary>
        /// A factory that can be used to create encodeable types.
        /// </summary>
        /// <value>The encodeable factory.</value>
        public IEncodeableFactory EncodeableFactory { get; set; } = null!;

        /// <summary>
        /// A factory that can be used to create node instances.
        /// </summary>
        /// <value>The node state factory.</value>
        public NodeStateFactory NodeStateFactory { get; set; }

        /// <summary>
        /// A factory that can be used to create node ids.
        /// </summary>
        /// <value>The node identifiers factory.</value>
        public INodeIdFactory NodeIdFactory { get; set; } = null!;

        /// <summary>
        /// The operation context associated with the system context.
        /// </summary>
        /// <value>The operation context.</value>
        public IOperationContext? OperationContext { get; protected set; }

        /// <summary>
        /// Creates a copy of the context that can be used with the specified operation context.
        /// </summary>
        /// <param name="context">The operation context to use.</param>
        /// <returns>
        /// A copy of the system context that references the new operation context.
        /// </returns>
        public ISystemContext Copy(IOperationContext context)
        {
            var copy = (SystemContext)MemberwiseClone();

            if (context != null)
            {
                copy.OperationContext = context;
            }

            return copy;
        }

        /// <summary>
        /// The diagnostics mask associated with the operation.
        /// </summary>
        /// <value>The diagnostics mask.</value>
        public DiagnosticsMasks DiagnosticsMask
        {
            get
            {
                if (OperationContext != null)
                {
                    return OperationContext.DiagnosticsMask;
                }

                return DiagnosticsMasks.None;
            }
        }

        /// <summary>
        /// The table of strings associated with the operation.
        /// </summary>
        /// <value>The string table.</value>
        public StringTable? StringTable
        {
            get
            {
                if (OperationContext != null)
                {
                    return OperationContext.StringTable;
                }

                return null;
            }
        }

        /// <summary>
        /// When the operation will be abandoned if it has not completed.
        /// </summary>
        /// <value>The operation deadline.</value>
        public DateTime OperationDeadline
        {
            get
            {
                if (OperationContext != null)
                {
                    return OperationContext.OperationDeadline;
                }

                return DateTime.MaxValue;
            }
        }

        /// <summary>
        /// The current status of the operation.
        /// </summary>
        /// <value>The operation status.</value>
        public StatusCode OperationStatus
        {
            get
            {
                if (OperationContext != null)
                {
                    return OperationContext.OperationStatus;
                }

                return StatusCodes.Good;
            }
        }

        private ArrayOf<string> m_preferredLocales;
        private string? m_auditEntryId;
    }

    /// <summary>
    /// System context extensions
    /// </summary>
    /// <remarks>
    /// When changing the name of the extension class or method names also update
    /// source generators.
    /// </remarks>
    public static class SystemContextExtensions
    {
        /// <summary>
        /// Convert an ISystemContext to an IServiceMessageContext
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public static IServiceMessageContext AsMessageContext(this ISystemContext context)
        {
            return new ServiceMessageContext(context.Telemetry, context.EncodeableFactory)
            {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris
            };
        }

        /// <summary>
        /// Returns the operation the system context was created for, or <c>null</c> when the
        /// context does not belong to an operation.
        /// <para>
        /// A callback that receives an <see cref="ISystemContext"/> uses this to hand its
        /// operation to an API that has to know which operation invoked it, without having to
        /// downcast to a concrete context type. A context built for the server itself rather than
        /// for a request carries no operation and returns <c>null</c>, as does a context that does
        /// not implement <see cref="IOperationContextProvider"/>.
        /// </para>
        /// </summary>
        /// <param name="context">The system context to read the operation from.</param>
        /// <returns>The operation context, or <c>null</c>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public static IOperationContext? GetOperationContext(this ISystemContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return (context as IOperationContextProvider)?.OperationContext;
        }
    }
}
