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

using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Types.BuiltIn
{
    /// <summary>
    /// Exercises generated encodeables consuming scalar Variants inside ExtensionObjects.
    /// </summary>
    [TestFixture]
    public sealed class VariantStorageGeneratedTests
    {
        [Test]
        public void GeneratedLiteralOperandsRoundTripEveryPackedScalarKind()
        {
            Variant[] inputs =
            [
                Variant.From(new QualifiedName("generated", 7)),
                Variant.From(new NodeId("generated", 7)),
                Variant.From(new ByteString(new byte[] { 1, 2, 3 })),
                Variant.From(new LocalizedText("generated"))
            ];
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            foreach (Variant input in inputs)
            {
                var literal = new LiteralOperand { Value = input };
                var nested = Variant.From(new ExtensionObject(literal));
                Assert.That(nested.Copy().GetExtensionObject().TryGetValue(out LiteralOperand copy), Is.True);
                Assert.That(copy.Value, Is.EqualTo(input));
                using var encoder = new BinaryEncoder(context);
                encoder.WriteVariant(null, nested);
                using var decoder = new BinaryDecoder(encoder.CloseAndReturnBuffer(), context);
                Variant decoded = decoder.ReadVariant(null);
                Assert.That(decoded.GetExtensionObject().TryGetValue(out LiteralOperand result), Is.True);
                Assert.That(result.Value.TypeInfo, Is.EqualTo(input.TypeInfo));
                Assert.That(result.Value, Is.EqualTo(input));
            }
        }
    }
}
