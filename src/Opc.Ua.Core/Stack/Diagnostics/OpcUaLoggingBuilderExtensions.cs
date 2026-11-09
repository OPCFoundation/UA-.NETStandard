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
