// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Default telemetry implementation
    /// </summary>
    public sealed class DefaultTelemetry : TelemetryContextBase, IDisposable
    {
        private readonly ILoggerFactory _ownedFactory;

        /// <summary>
        /// Create default telemetry
        /// </summary>
        private DefaultTelemetry(ILoggerFactory ownedFactory)
            : base(ownedFactory)
        {
            _ownedFactory = ownedFactory;
        }

        /// <summary>
        /// Create default telemetry
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static ITelemetryContext Create(Action<ILoggingBuilder> configure)
        {
            ILoggerFactory? factory = null;
            try
            {
                factory = Microsoft.Extensions.Logging.LoggerFactory.Create(logging =>
                {
                    logging.EnableOpcUaRedaction();
                    configure(logging);
                });
                var result = new DefaultTelemetry(factory);
                factory = null;
                return result;
            }
            finally
            {
                factory?.Dispose();
            }
        }

        /// <summary>
        /// Dispose the owned logger factory.
        /// </summary>
        public void Dispose()
        {
            _ownedFactory?.Dispose();
        }
    }
}
