/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
                Assert.That(nested.Copy().GetExtensionObject().TryGetValue(out LiteralOperand? copy), Is.True);
                Assert.That(copy!.Value, Is.EqualTo(input));
                using var encoder = new BinaryEncoder(context);
                encoder.WriteVariant(null, nested);
                using var decoder = new BinaryDecoder(encoder.CloseAndReturnBuffer()!, context);
                Variant decoded = decoder.ReadVariant(null);
                Assert.That(decoded.GetExtensionObject().TryGetValue(out LiteralOperand? result), Is.True);
                Assert.That(result!.Value.TypeInfo, Is.EqualTo(input.TypeInfo));
                Assert.That(result.Value, Is.EqualTo(input));
            }
        }
    }
}
