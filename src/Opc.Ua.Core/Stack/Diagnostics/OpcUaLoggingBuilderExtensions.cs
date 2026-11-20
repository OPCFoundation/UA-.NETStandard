// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Microsoft.Extensions.Logging
{
    /// <summary>
    /// Logging builder extensions for the data classification of the OPC UA stack.
    /// </summary>
    public static class OpcUaLoggingBuilderExtensions
    {
        /// <summary>
        /// Enables the redaction-aware logging pipeline that the stack's classified
        /// log parameters (see <c>Opc.Ua.Redaction.OpcUaDataClassifications</c>) need.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The stack logs network addresses, application and user identities, file
        /// paths and exception texts as classified parameters. A plain
        /// <see cref="ILoggerFactory"/> drops classified values; this method enables
        /// the extended logger (<c>EnableRedaction()</c>) so they are logged.
        /// </para>
        /// <para>
        /// Values are logged unchanged unless redactors are registered with
        /// <c>services.AddRedaction(...)</c> (package
        /// <c>Microsoft.Extensions.Compliance.Redaction</c>), e.g. an
        /// <c>ErasingRedactor</c> for <c>OpcUaDataClassifications.NetworkAddress</c>.
        /// Once any redactor is registered, every classification without its own
        /// redactor is erased by the fallback redactor.
        /// </para>
        /// <para>
        /// The redaction discriminator is turned off, so values that are not redacted
        /// are logged exactly as before. Applications that use an HMAC redactor and want
        /// a discriminator configure <see cref="LoggerRedactionOptions"/> afterwards.
        /// </para>
        /// <para>
        /// The stack calls this method in <c>AddOpcUa().AddLogging(...)</c> and
        /// <c>DefaultTelemetry.Create(...)</c>. Applications that pass their own
        /// <see cref="ILoggerFactory"/> to the stack call it while building that factory.
        /// </para>
        /// </remarks>
        /// <param name="builder">The logging builder.</param>
        /// <returns>The same <paramref name="builder"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static ILoggingBuilder EnableOpcUaRedaction(this ILoggingBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            return builder.EnableRedaction(options => options.ApplyDiscriminator = false);
        }
    }
}
