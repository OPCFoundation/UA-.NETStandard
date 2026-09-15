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

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Test;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Types
{
    /// <summary>
    /// Tests for <see cref="DataGenerator"/>.
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [Parallelizable]
    public class DataGeneratorTests
    {
        /// <summary>
        /// A caller that asks for no boundary values must not be handed one.
        /// <see cref="NodeId.Null"/> is itself listed among the boundary values
        /// the generator offers, yet the random draw could produce it directly:
        /// an opaque identifier in namespace 0 is null when its byte string is
        /// empty, and the length is drawn from a range that includes zero.
        /// </summary>
        /// <remarks>
        /// The failure this guards against is rare per call, so a single draw
        /// proves nothing. The loop is sized to make the pre-fix behaviour
        /// practically certain to appear: with roughly one draw in 130 landing
        /// on the empty opaque identifier, 20 000 draws would have produced
        /// well over a hundred null node ids.
        /// </remarks>
        [Test]
        public void GetRandomNodeIdWithoutBoundaryValuesIsNeverNull()
        {
            var generator = new DataGenerator(new RandomSource(42), NUnitTelemetryContext.Create());

            for (int ii = 0; ii < 20000; ii++)
            {
                NodeId nodeId = generator.GetRandomNodeId();

                Assert.That(nodeId.IsNull, Is.False,
                    $"GetRandomNodeId returned NodeId.Null on draw {ii}, " +
                    "although boundary values were not requested.");
            }
        }

        /// <summary>
        /// <see cref="DataGenerator.GetRandomExpandedNodeId"/> builds on
        /// <see cref="DataGenerator.GetRandomNodeId"/>, so it inherits the same
        /// guarantee.
        /// </summary>
        [Test]
        public void GetRandomExpandedNodeIdWithoutBoundaryValuesIsNeverNull()
        {
            var generator = new DataGenerator(null, NUnitTelemetryContext.Create());

            for (int ii = 0; ii < 20000; ii++)
            {
                ExpandedNodeId nodeId = generator.GetRandomExpandedNodeId();

                Assert.That(nodeId.IsNull, Is.False,
                    $"GetRandomExpandedNodeId returned ExpandedNodeId.Null on draw {ii}, " +
                    "although boundary values were not requested.");
            }
        }

        /// <summary>
        /// The guarantee above must not cost the generator its variety: every
        /// identifier type should still appear. Without this a fix that simply
        /// stopped emitting opaque identifiers would pass the tests above while
        /// silently narrowing what the generator produces.
        /// </summary>
        [Test]
        public void GetRandomNodeIdStillProducesEveryIdentifierType()
        {
            var generator = new DataGenerator(null, NUnitTelemetryContext.Create());
            var seen = new HashSet<IdType>();

            for (int ii = 0; ii < 20000; ii++)
            {
                seen.Add(generator.GetRandomNodeId().IdType);
            }

            Assert.That(seen, Does.Contain(IdType.Numeric));
            Assert.That(seen, Does.Contain(IdType.String));
            Assert.That(seen, Does.Contain(IdType.Guid));
            Assert.That(seen, Does.Contain(IdType.Opaque));
        }

        /// <summary>
        /// Asking for boundary values must still offer them, otherwise the fix
        /// would have removed the feature rather than corrected its leak into
        /// the non-boundary path.
        /// </summary>
        [Test]
        public void GetRandomNodeIdWithBoundaryValuesStillYieldsThem()
        {
            var generator = new DataGenerator(null, NUnitTelemetryContext.Create());
            bool sawNull = false;

            for (int ii = 0; ii < 20000 && !sawNull; ii++)
            {
                sawNull = generator.GetRandomNodeId(true).IsNull;
            }

            Assert.That(sawNull, Is.True,
                "GetRandomNodeId(useBoundaryValues: true) never returned a null " +
                "node id, so the boundary values are no longer reachable.");
        }

        /// <summary>
        /// The random bits are read back as a double. They used to be read back
        /// as a float and widened, so the generator never produced a value
        /// outside the float range and the whole upper exponent range of the
        /// type went untested by everything built on it.
        /// </summary>
        [Test]
        public void GetRandomDoubleCoversTheFullDoubleRange()
        {
            var generator = new DataGenerator(new RandomSource(42), NUnitTelemetryContext.Create());
            bool sawBeyondSingleRange = false;

            for (int ii = 0; ii < 2000 && !sawBeyondSingleRange; ii++)
            {
                double value = generator.GetRandomDouble();

                sawBeyondSingleRange = !double.IsNaN(value) &&
                    !double.IsInfinity(value) &&
                    System.Math.Abs(value) > float.MaxValue;
            }

            Assert.That(sawBeyondSingleRange, Is.True,
                "GetRandomDouble never produced a finite value outside the range " +
                "of a float, so it is still reading the random bits as a float.");
        }

        /// <summary>
        /// The typed array generators honour the type they picked. They used to
        /// pick one and then fill the array from the unconstrained variant
        /// generator, so an "integer array" could come back full of strings and
        /// date times.
        /// </summary>
        [TestCaseSource(nameof(TypedArrayCases))]
        public void TypedVariantArraysHoldOnlyTheirOwnTypes(
            string name,
            BuiltInType[] allowed)
        {
            var generator = new DataGenerator(new RandomSource(42), NUnitTelemetryContext.Create());

            for (int attempt = 0; attempt < 50; attempt++)
            {
                Variant[] values = GenerateTypedArray(generator, name);

                foreach (Variant value in values)
                {
                    Assert.That(
                        value.TypeInfo.BuiltInType,
                        Is.AnyOf(allowed),
                        $"{name} produced a {value.TypeInfo.BuiltInType} element.");
                }
            }
        }

        private static Variant[] GenerateTypedArray(DataGenerator generator, string name)
        {
            return name switch
            {
                nameof(DataGenerator.GetRandomUIntegerArray) =>
                    generator.GetRandomUIntegerArray(false, 16, true),
                nameof(DataGenerator.GetRandomIntegerArray) =>
                    generator.GetRandomIntegerArray(false, 16, true),
                _ => generator.GetRandomNumberArray(false, 16, true)
            };
        }

        public static IEnumerable<object[]> TypedArrayCases()
        {
            yield return
            [
                nameof(DataGenerator.GetRandomUIntegerArray),
                new[]
                {
                    BuiltInType.Byte,
                    BuiltInType.UInt16,
                    BuiltInType.UInt32,
                    BuiltInType.UInt64
                }
            ];

            yield return
            [
                nameof(DataGenerator.GetRandomIntegerArray),
                new[]
                {
                    BuiltInType.SByte,
                    BuiltInType.Int16,
                    BuiltInType.Int32,
                    BuiltInType.Int64
                }
            ];

            yield return
            [
                nameof(DataGenerator.GetRandomNumberArray),
                new[]
                {
                    BuiltInType.SByte,
                    BuiltInType.Byte,
                    BuiltInType.Int16,
                    BuiltInType.UInt16,
                    BuiltInType.Int32,
                    BuiltInType.UInt32,
                    BuiltInType.Int64,
                    BuiltInType.UInt64,
                    BuiltInType.Float,
                    BuiltInType.Double
                }
            ];
        }
    }
}
