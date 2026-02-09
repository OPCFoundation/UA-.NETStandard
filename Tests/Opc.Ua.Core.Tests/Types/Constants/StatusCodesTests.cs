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

namespace Opc.Ua.Core.Tests.Types.Constants
{
    /// <summary>
    /// Tests for the StatusCodes class helper methods.
    /// </summary>
    [TestFixture]
    [Category("StatusCodes")]
    [Parallelizable]
    public class StatusCodesTests
    {
        /// <summary>
        /// Test GetBrowseName for standard status codes.
        /// </summary>
        [Test]
        public void StatusCode_SymbolicId_StandardStatusCodes_ReturnsValidNames()
        {
            // Test a few standard status code IDs
            StatusCode[] statusCodeIds = [
                StatusCodes.Good,
                StatusCodes.Bad,
                StatusCodes.Uncertain,
                StatusCodes.BadNodeIdUnknown,
                StatusCodes.BadAttributeIdInvalid,
                StatusCodes.BadIndexRangeInvalid,
                StatusCodes.BadTypeMismatch,
                StatusCodes.GoodResultsMayBeIncomplete,
                StatusCodes.UncertainReferenceOutOfServer
            ];

            foreach (StatusCode id in statusCodeIds)
            {
                string browseName = id.SymbolicId;
                Assert.That(browseName, Is.Not.Null);
                Assert.That(browseName, Is.Not.Empty);
            }
        }

        /// <summary>
        /// Test GetBrowseName for Good status code.
        /// </summary>
        [Test]
        public void StatusCode_SymbolicId_GoodStatusCode_ReturnsGood()
        {
            string browseName = StatusCodes.Good.SymbolicId;
            Assert.That(browseName, Is.EqualTo("Good"));
        }

        /// <summary>
        /// Test GetBrowseName for Bad status code.
        /// </summary>
        [Test]
        public void StatusCode_SymbolicId_BadStatusCode_ReturnsBad()
        {
            string browseName = StatusCodes.Bad.SymbolicId;
            Assert.That(browseName, Is.EqualTo("Bad"));
        }

        /// <summary>
        /// Test GetBrowseName for invalid status code ID returns empty string.
        /// </summary>
        [Test]
        public void StatusCode_SymbolicId_InvalidStatusCodeId_ReturnsEmptyString()
        {
            string browseName = new StatusCode(0x12345678).SymbolicId;
            Assert.That(browseName, Is.Null);
        }
    }
}
