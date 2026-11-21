// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// The binding for the UA native stack
    /// </summary>
    public abstract class BaseBinding
    {
        /// <summary>
        /// Initializes the binding.
        /// </summary>
        protected BaseBinding(
            NamespaceTable namespaceUris,
            IEncodeableFactory factory,
            EndpointConfiguration configuration,
            ITelemetryContext telemetry)
        {
            MessageContext = new ServiceMessageContext(telemetry, factory)
            {
                MaxStringLength = configuration.MaxStringLength,
                MaxByteStringLength = configuration.MaxByteStringLength,
                MaxArrayLength = configuration.MaxArrayLength,
                MaxMessageSize = configuration.MaxMessageSize,
                MaxEncodingNestingLevels = configuration.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = configuration.MaxDecoderRecoveries,
                NamespaceUris = namespaceUris
            };
        }

        /// <summary>
        /// The message context to use with the binding.
        /// </summary>
        public IServiceMessageContext MessageContext { get; set; }
    }
}
