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

using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Types.BuiltIn
{
    [TestFixture]
    [Category("BuiltIn")]
    [Parallelizable(ParallelScope.All)]
    public sealed class SessionlessLocaleRegressionTests
    {
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
    }
}
