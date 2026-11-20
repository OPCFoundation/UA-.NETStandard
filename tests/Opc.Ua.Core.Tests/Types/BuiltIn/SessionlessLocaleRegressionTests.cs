// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
            [Values(null, "")] string? entry)
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.ServerUris.Append("urn:local");
            byte[] encoded;
            if (json)
            {
                using var encoder = new JsonEncoder(context, JsonEncoderOptions.Verbose);
                WriteMalformedTables(encoder, table, entry!);
                encoded = Encoding.UTF8.GetBytes(encoder.CloseAndReturnText());
            }
            else
            {
                using var encoder = new BinaryEncoder(context);
                encoder.WriteNodeId(null, DataTypeIds.SessionlessInvokeRequestType);
                WriteMalformedTables(encoder, table, entry!);
                encoded = encoder.CloseAndReturnBuffer()!;
            }

            Assert.That(
                () => json
                    ? SessionLessMessage.DecodeAsJson(encoded!, context)
                    : SessionLessMessage.DecodeAsBinary(encoded!, context),
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
            byte[] encoded = encoder.CloseAndReturnBuffer()!;
            using var decoder = new BinaryDecoder(encoded!, context);
            var restored = new SessionLessServiceMessage();
            restored.Decode(decoder);

            Assert.That(restored.UriVersion, Is.EqualTo(7));
            Assert.That(restored.LocaleIds!.ToArray(), Is.EqualTo(locales.ToArray()));
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
