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

namespace Opc.Ua
{
    /// <summary>
    /// An interface to an object that describes how access the system containing the data.
    /// </summary>
    public interface ISessionSystemContext : ISystemContext
    {
        /// <summary>
        /// The identifier for the session (null if multiple sessions are associated with the operation).
        /// </summary>
        /// <value>The session identifier.</value>
        NodeId? SessionId { get; }

        /// <summary>
        /// The identity of the user (null if not available).
        /// </summary>
        /// <value>The user identity.</value>
        IUserIdentity? UserIdentity { get; }
    }

    /// <summary>
    /// A generic implementation for ISystemContext interface.
    /// </summary>
    public class SessionSystemContext : ISessionSystemContext, ISessionOperationContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SessionSystemContext"/> class.
        /// </summary>
        public SessionSystemContext(ITelemetryContext telemetry)
        {
            m_values = new SharedValues(telemetry, this);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SessionSystemContext"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public SessionSystemContext(IOperationContext context, ITelemetryContext telemetry)
        {
            m_values = new SharedValues(telemetry, this);
            OperationContext = context;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SessionSystemContext"/> class
        /// with the values of another context.
        /// </summary>
        /// <remarks>
        /// Copying with a constructor allocates through the JIT's allocation fast path.
        /// <see cref="object.MemberwiseClone"/> allocates through a runtime call, which
        /// costs more on the per-request path of a server.
        /// <para>
        /// The copy shares the values of the source (namespace and server tables, type
        /// table, factories, handle, session values) until either of them changes one: a
        /// server copies its node managers' contexts for every request, and the copy then
        /// only holds its operation context and a reference to the shared values.
        /// </para>
        /// </remarks>
        /// <param name="source">The context to copy.</param>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <c>null</c>.</exception>
        protected SessionSystemContext(SessionSystemContext source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            m_values = source.Share();
            OperationContext = source.OperationContext;
        }

        /// <inheritdoc/>
        public ITelemetryContext Telemetry => m_values.Telemetry;

        /// <summary>
        /// An application defined handle for the system.
        /// </summary>
        /// <value>The system handle.</value>
        public object? SystemHandle
        {
            get => m_values.SystemHandle;
            set => Writable().SystemHandle = value;
        }

        /// <inheritdoc/>
        public string? UserId => UserIdentity?.DisplayName;

        /// <summary>
        /// The identifier for the session (null if multiple sessions are associated with the operation).
        /// </summary>
        /// <value>The session identifier.</value>
        public NodeId? SessionId
        {
            get
            {
                if (OperationContext is ISessionOperationContext session)
                {
                    return session.SessionId;
                }

                return m_values.SessionId;
            }
            set => Writable().SessionId = value;
        }

        /// <summary>
        /// The identity of the user.
        /// </summary>
        /// <value>The user identity.</value>
        public IUserIdentity? UserIdentity
        {
            get
            {
                if (OperationContext is ISessionOperationContext session)
                {
                    return session.UserIdentity;
                }

                return m_values.UserIdentity;
            }
            set => Writable().UserIdentity = value;
        }

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

                return m_values.PreferredLocales;
            }
            set => Writable().PreferredLocales = value;
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

                return m_values.AuditEntryId;
            }
            set => Writable().AuditEntryId = value;
        }

        /// <summary>
        /// The table of namespace uris to use when accessing the system.
        /// </summary>
        /// <value>The namespace URIs.</value>
        public NamespaceTable NamespaceUris
        {
            get => m_values.NamespaceUris;
            set => Writable().NamespaceUris = value;
        }

        /// <summary>
        /// The table of server uris to use when accessing the system.
        /// </summary>
        /// <value>The server URIs.</value>
        public StringTable ServerUris
        {
            get => m_values.ServerUris;
            set => Writable().ServerUris = value;
        }

        /// <summary>
        /// A table containing the types that are to be used when accessing the system.
        /// </summary>
        /// <value>The type table.</value>
        public ITypeTable TypeTable
        {
            get => m_values.TypeTable;
            set => Writable().TypeTable = value;
        }

        /// <summary>
        /// A factory that can be used to create encodeable types.
        /// </summary>
        /// <value>The encodeable factory.</value>
        public IEncodeableFactory EncodeableFactory
        {
            get => m_values.EncodeableFactory;
            set => Writable().EncodeableFactory = value;
        }

        /// <summary>
        /// A factory that can be used to create node instances.
        /// </summary>
        /// <value>The node state factory.</value>
        public NodeStateFactory NodeStateFactory
        {
            get => m_values.NodeStateFactory;
            set => Writable().NodeStateFactory = value;
        }

        /// <summary>
        /// A factory that can be used to create node ids.
        /// </summary>
        /// <value>The node identifiers factory.</value>
        public INodeIdFactory NodeIdFactory
        {
            get => m_values.NodeIdFactory;
            set => Writable().NodeIdFactory = value;
        }

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
            var copy = (SessionSystemContext)MemberwiseCloneSharingValues();

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

        /// <summary>
        /// The lifetime of the request
        /// </summary>
        public RequestLifetime RequestLifetime
        {
            get
            {
                if (OperationContext is ISessionOperationContext session)
                {
                    return session.RequestLifetime;
                }

                return RequestLifetime.None;
            }
        }

        /// <summary>
        /// ISessionOperationContext requires non-nullable SessionId and
        /// UserIdentity.
        /// </summary>
        NodeId ISessionOperationContext.SessionId => SessionId ?? NodeId.Null;
        IUserIdentity ISessionOperationContext.UserIdentity => UserIdentity!;

        /// <summary>
        /// Returns a member-wise copy of the context whose values are shared with this
        /// one until either changes one.
        /// </summary>
        /// <remarks>
        /// Use it instead of <see cref="object.MemberwiseClone"/> in derived types: a
        /// plain member-wise copy shares the values too, but this context would keep
        /// changing them in place, so the copy would see later changes of this one.
        /// </remarks>
        protected object MemberwiseCloneSharingValues()
        {
            Share();
            return MemberwiseClone();
        }

        /// <summary>
        /// Marks the values as shared, so that neither this context nor a copy changes
        /// them in place, and returns them.
        /// </summary>
        private SharedValues Share()
        {
            SharedValues values = m_values;

            // written once; after that, copying for a request writes nothing to the
            // template, which every request of a node manager reads.
            if (values.Owner != null)
            {
                values.Owner = null;
            }

            return values;
        }

        /// <summary>
        /// Returns values that this context may change: its own, or a copy of shared ones.
        /// </summary>
        private SharedValues Writable()
        {
            SharedValues values = m_values;
            if (!ReferenceEquals(values.Owner, this))
            {
                values = values.CopyFor(this);
                m_values = values;
            }

            return values;
        }

        /// <summary>
        /// The values a context copies from its template, held apart from the context so
        /// that a copy shares them instead of copying each one.
        /// </summary>
        private sealed class SharedValues
        {
            public SharedValues(ITelemetryContext telemetry, SessionSystemContext owner)
            {
                Telemetry = telemetry;
                NodeStateFactory = new NodeStateFactory();
                Owner = owner;
            }

            /// <summary>
            /// The only context that changes the values in place, or <c>null</c> once
            /// they are shared.
            /// </summary>
            public SessionSystemContext? Owner { get; set; }

            public ITelemetryContext Telemetry { get; }
            public object? SystemHandle { get; set; }
            public NamespaceTable NamespaceUris { get; set; } = null!;
            public StringTable ServerUris { get; set; } = null!;
            public ITypeTable TypeTable { get; set; } = null!;
            public IEncodeableFactory EncodeableFactory { get; set; } = null!;
            public NodeStateFactory NodeStateFactory { get; set; }
            public INodeIdFactory NodeIdFactory { get; set; } = null!;
            public NodeId? SessionId { get; set; }
            public ArrayOf<string> PreferredLocales { get; set; }
            public string? AuditEntryId { get; set; }
            public IUserIdentity? UserIdentity { get; set; }

            public SharedValues CopyFor(SessionSystemContext owner)
            {
                var copy = (SharedValues)MemberwiseClone();
                copy.Owner = owner;
                return copy;
            }
        }

        private SharedValues m_values;
    }
}
