/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System.Text;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Types.BuiltIn
{
    /// <summary>
    /// Verifies sessionless binary messages retain locale tables without dropping entries.
    /// </summary>
    [TestFixture]
    [Category("BuiltIn")]
    [Parallelizable(ParallelScope.All)]
    public sealed class SessionlessLocaleRegressionTests
    {
        [Test]
        [Combinatorial]
        public void MalformedSessionlessTableEntryReturnsBadDecodingError(
            [Values] bool json,
            [Values("NamespaceUris", "ServerUris", "LocaleIds")] string table,
            [Values(null, "")] string entry)
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.ServerUris.Append("urn:local");
            byte[] encoded;
            if (json)
            {
                using var encoder = new JsonEncoder(context, JsonEncoderOptions.Verbose);
                WriteMalformedTables(encoder, table, entry);
                encoded = Encoding.UTF8.GetBytes(encoder.CloseAndReturnText());
            }
            else
            {
                using var encoder = new BinaryEncoder(context);
                encoder.WriteNodeId(null, DataTypeIds.SessionlessInvokeRequestType);
                WriteMalformedTables(encoder, table, entry);
                encoded = encoder.CloseAndReturnBuffer();
            }

            Assert.That(
                () => json
                    ? SessionLessMessage.DecodeAsJson(encoded, context)
                    : SessionLessMessage.DecodeAsBinary(encoded, context),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadDecodingError));
        }

        /// <summary>
        /// Verifies empty, single-entry, and multi-entry locale tables survive binary encoding and decoding.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void SessionlessBinaryRoundTripPreservesEveryLocale(int count)
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var locales = new StringTable();
            if (count > 0)
            {
                locales.Append("en-US");
            }
            if (count > 1)
            {
                locales.Append("de-DE");
            }
            var message = new SessionLessServiceMessage
            {
                UriVersion = 7,
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                LocaleIds = locales
            };
            using var encoder = new BinaryEncoder(context);
            message.Encode(encoder);
            byte[] encoded = encoder.CloseAndReturnBuffer();
            using var decoder = new BinaryDecoder(encoded, context);
            var restored = new SessionLessServiceMessage();
            restored.Decode(decoder);

            Assert.That(restored.UriVersion, Is.EqualTo(7));
            Assert.That(restored.LocaleIds.ToArray(), Is.EqualTo(locales.ToArray()));
        }

        private static void WriteMalformedTables(IEncoder encoder, string table, string entry)
        {
            encoder.WriteUInt32("UriVersion", 0);
            encoder.WriteStringArray("NamespaceUris", table == "NamespaceUris" ? [entry] : []);
            encoder.WriteStringArray("ServerUris", table == "ServerUris" ? [entry] : []);
            encoder.WriteStringArray("LocaleIds", table == "LocaleIds" ? [entry] : []);
            encoder.WriteUInt32("ServiceId", 0);
        }
    }
}
