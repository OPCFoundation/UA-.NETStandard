/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Stores context information associated with a session is used during message processing.
    /// </summary>
    public class ServiceMessageContext : IServiceMessageContext
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        [Obsolete("Use ServiceMessageContext(ITelemetryContext) instead.")]
        public ServiceMessageContext()
            : this(null)
        {
        }

        /// <summary>
        /// Initializes the object with default values and a new encodeable factory.
        /// </summary>
        /// <param name="telemetry">The telemetry context to use to create observability instruments.</param>
        /// <remarks>
        /// A new <see cref="IEncodeableFactory"/> instance is created via <see cref="EncodeableFactory.Create()"/>.
        /// To use a private factory that is isolated from other instances in the same process,
        /// use <see cref="ServiceMessageContext(ITelemetryContext, IEncodeableFactory)"/> instead.
        /// </remarks>
        public ServiceMessageContext(ITelemetryContext telemetry)
            : this(telemetry, null)
        {
        }

        /// <summary>
        /// Initializes the object with default values and an optional private encodeable factory.
        /// </summary>
        /// <param name="telemetry">The telemetry context to use to create observability instruments.</param>
        /// <param name="factory">The private encodeable factory to use. If null, a new factory will be created.</param>
        public ServiceMessageContext(ITelemetryContext telemetry, IEncodeableFactory factory)
        {
            Telemetry = telemetry;
            MaxStringLength = DefaultEncodingLimits.MaxStringLength;
            MaxByteStringLength = DefaultEncodingLimits.MaxByteStringLength;
            MaxArrayLength = DefaultEncodingLimits.MaxArrayLength;
            MaxMessageSize = DefaultEncodingLimits.MaxMessageSize;
            MaxEncodingNestingLevels = DefaultEncodingLimits.MaxEncodingNestingLevels;
            MaxDecoderRecoveries = DefaultEncodingLimits.MaxDecoderRecoveries;
            m_namespaceUris = new NamespaceTable();
            m_serverUris = new StringTable();
            m_factory = factory ?? EncodeableFactory.Create();
        }

        /// <summary>
        /// Create a copy of the provided context
        /// </summary>
        /// <param name="context"></param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public ServiceMessageContext(IServiceMessageContext context, ITelemetryContext telemetry)
        {
            Telemetry = context.Telemetry ?? telemetry;
            MaxStringLength = context.MaxStringLength;
            MaxByteStringLength = context.MaxByteStringLength;
            MaxArrayLength = context.MaxArrayLength;
            MaxMessageSize = context.MaxMessageSize;
            MaxEncodingNestingLevels = context.MaxEncodingNestingLevels;
            MaxDecoderRecoveries = context.MaxDecoderRecoveries;
            m_namespaceUris = new NamespaceTable(context.NamespaceUris);
            m_serverUris = new StringTable(context.ServerUris);
            m_factory = context.Factory;
        }

        /// <summary>
        /// The default context for the process (used only during XML serialization).
        /// </summary>
        [Obsolete("Create a new root ServiceMessageContext or a copy of an existing one for a scope.")]
        public static ServiceMessageContext GlobalContext { get; } = new(null);

        /// <summary>
        /// The default context for the thread (used only during XML serialization).
        /// </summary>
        [Obsolete("Create a new root ServiceMessageContext or a copy of an existing one for a scope.")]
        public static ServiceMessageContext ThreadContext
        {
            get => GlobalContext;
            set
            {
            }
        }

        /// <inheritdoc/>
        public ITelemetryContext Telemetry { get; }

        /// <inheritdoc/>
        public int MaxStringLength { get; set; }

        /// <inheritdoc/>
        public int MaxArrayLength { get; set; }

        /// <inheritdoc/>
        public int MaxByteStringLength { get; set; }

        /// <inheritdoc/>
        public int MaxMessageSize { get; set; }

        /// <inheritdoc/>
        public int MaxEncodingNestingLevels { get; set; }

        /// <inheritdoc/>
        public int MaxDecoderRecoveries { get; set; }

        /// <inheritdoc/>
        public NamespaceTable NamespaceUris
        {
            get => m_namespaceUris;
            set
            {
                if (value == null)
                {
                    m_namespaceUris = new NamespaceTable();
                    return;
                }
                m_namespaceUris = value;
            }
        }

        /// <inheritdoc/>
        public StringTable ServerUris
        {
            get => m_serverUris;
            set
            {
                if (value == null)
                {
                    m_serverUris = new StringTable();
                    return;
                }

                m_serverUris = value;
            }
        }

        /// <inheritdoc/>
        public IEncodeableFactory Factory
        {
            get => m_factory;
            set
            {
                if (value == null)
                {
                    m_factory = EncodeableFactory.Create();
                    return;
                }

                m_factory = value;
            }
        }

        private NamespaceTable m_namespaceUris;
        private StringTable m_serverUris;
        private IEncodeableFactory m_factory;
    }
}
