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
using Opc.Ua;
using Opc.Ua.OpenUsd.Client;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Pins which source values the connector's §5.8 translation profile accepts.
    /// </summary>
    /// <remarks>
    /// This matters more than it looks. The profile fails closed - an unaccepted value
    /// leaves the target unresolved with no error anywhere - so a server publishing a
    /// position in a shape the profile does not take produces a viewport that silently
    /// never moves the prim, while every subscription counter says the data is flowing.
    /// </remarks>
    [TestFixture]
    [Category("OpenUsd")]
    [Parallelizable]
    public sealed class OpenUsdTranslationProfileTests
    {
        [Test]
        public void StructuredCartesianCoordinatesAreAccepted()
        {
            var binding = new OpenUsdConnector.BindingInfo
            {
                Kind = OpenUsdRenderTargetKind.Translation,
                Scale = 1.0,
                Offset = 0.0
            };
            var value = new Variant(new ExtensionObject(new ThreeDCartesianCoordinates
            {
                X = 1.5,
                Y = -2.5,
                Z = 3.5
            }));

            Variant converted = OpenUsdConnector.Convert(binding, value);

            Assert.That(converted.IsNull, Is.False,
                "A structured 3D coordinate is the source shape the translation profile is " +
                "defined for; leaving it unresolved would stop any prim following it.");
            Assert.That(converted.ToString(), Does.Contain("1.5"));
        }

        [Test]
        public void PlainDoubleArrayIsNotAccepted()
        {
            var binding = new OpenUsdConnector.BindingInfo
            {
                Kind = OpenUsdRenderTargetKind.Translation,
                Scale = 1.0,
                Offset = 0.0
            };

            Variant converted = OpenUsdConnector.Convert(binding, new Variant(new[] { 1.5, -2.5, 3.5 }));

            Assert.That(converted.IsNull, Is.True,
                "A bare double[3] is not a structured 3D source. This is the behaviour that " +
                "silently stopped the bin-picking parts moving, so it is pinned here to " +
                "make the requirement visible rather than surprising.");
        }
    }
}
