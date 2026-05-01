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

#nullable enable

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
        [Obsolete("Use ServiceMessageContext.Create(ITelemetryContext) instead.")]
        public ServiceMessageContext()
            : this(null!)
        {
        }

        /// <summary>
        /// Initializes the object with default values and a new encodeable
        /// factory. Obsolete - use the factory method to create a context
        /// safely as this constructor might create a message context with
        /// an empty encodeable factory.
        /// </summary>
        [Obsolete("Use ServiceMessageContext.Create(ITelemetryContext) instead.")]
        public ServiceMessageContext(ITelemetryContext telemetry)
            : this(telemetry, null!)
        {
        }

        /// <summary>
        /// Initializes the object with default values and a encodeable factory.
        /// </summary>
        /// <param name="telemetry">The telemetry context to use to create
        /// observability instruments.</param>
        /// <param name="factory">The private encodeable factory to use.</param>
        public ServiceMessageContext(ITelemetryContext telemetry, IEncodeableFactory factory)
        {
            Telemetry = telemetry;
            Factory = factory;
            MaxStringLength = DefaultEncodingLimits.MaxStringLength;
            MaxByteStringLength = DefaultEncodingLimits.MaxByteStringLength;
            MaxArrayLength = DefaultEncodingLimits.MaxArrayLength;
            MaxMessageSize = DefaultEncodingLimits.MaxMessageSize;
            MaxEncodingNestingLevels = DefaultEncodingLimits.MaxEncodingNestingLevels;
            MaxDecoderRecoveries = DefaultEncodingLimits.MaxDecoderRecoveries;
            NamespaceUris = new NamespaceTable();
            ServerUris = new StringTable();
        }

        /// <summary>
        /// Create a copy of the provided context
        /// </summary>
        /// <param name="context"></param>
        /// <param name="telemetry">The telemetry context to use to create
        /// obvservability instruments</param>
        public ServiceMessageContext(IServiceMessageContext context, ITelemetryContext telemetry)
        {
            Telemetry = context.Telemetry ?? telemetry;
            Factory = context.Factory;
            MaxStringLength = context.MaxStringLength;
            MaxByteStringLength = context.MaxByteStringLength;
            MaxArrayLength = context.MaxArrayLength;
            MaxMessageSize = context.MaxMessageSize;
            MaxEncodingNestingLevels = context.MaxEncodingNestingLevels;
            MaxDecoderRecoveries = context.MaxDecoderRecoveries;
            NamespaceUris = new NamespaceTable(context.NamespaceUris);
            ServerUris = new StringTable(context.ServerUris);
        }

        /// <summary>
        /// Creates a new context with default values and an empty encodeable factory.
        /// </summary>
        /// <param name="telemetry">The telemetry context to use to create
        /// observability instruments.</param>
        /// <returns>A new instance of <see cref="ServiceMessageContext"/>.</returns>
        public static ServiceMessageContext CreateEmpty(ITelemetryContext telemetry)
        {
            return new ServiceMessageContext(telemetry, new EncodeableFactory());
        }

        /// <summary>
        /// The default context for the process (used only during XML serialization).
        /// </summary>
        [Obsolete("Use CreateEmpty() to create an empty context or a copy of an existing one for a scope.")]
        public static ServiceMessageContext GlobalContext { get; } = new(null!);

        /// <summary>
        /// The default context for the thread (used only during XML serialization).
        /// </summary>
        [Obsolete("Use CreateEmpty() to create an empty context or a copy of an existing one for a scope.")]
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
            get => field;
            set
            {
                if (value == null)
                {
                    field = new NamespaceTable();
                    return;
                }
                field = value;
            }
        }

        /// <inheritdoc/>
        public StringTable ServerUris
        {
            get => field;
            set
            {
                if (value == null)
                {
                    field = new StringTable();
                    return;
                }

                field = value;
            }
        }

        /// <inheritdoc/>
        public IEncodeableFactory Factory
        {
            get => field;
            private set
            {
                if (value == null)
                {
                    field = EncodeableFactory.Root.IsValueCreated ?
                        EncodeableFactory.Root.Value :
                        new EncodeableFactory();
                    return;
                }

                field = value;
            }
        }
    }
}
