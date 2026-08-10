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
    }
}
