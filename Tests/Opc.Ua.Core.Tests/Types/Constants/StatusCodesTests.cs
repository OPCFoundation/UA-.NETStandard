/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

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
        public void GetBrowseName_StandardStatusCodes_ReturnsValidNames()
        {
            // Test a few standard status code IDs
            uint[] statusCodeIds = {
                StatusCodes.Good,
                StatusCodes.Bad,
                StatusCodes.Uncertain,
                StatusCodes.BadNodeIdUnknown,
                StatusCodes.BadAttributeIdInvalid,
                StatusCodes.BadIndexRangeInvalid,
                StatusCodes.BadTypeMismatch,
                StatusCodes.GoodResultsMayBeIncomplete,
                StatusCodes.UncertainReferenceOutOfServer
            };

            foreach (uint id in statusCodeIds)
            {
                string browseName = StatusCodes.GetBrowseName(id);
                Assert.IsNotNull(browseName);
                Assert.IsNotEmpty(browseName);
            }
        }

        /// <summary>
        /// Test GetBrowseName for Good status code.
        /// </summary>
        [Test]
        public void GetBrowseName_GoodStatusCode_ReturnsGood()
        {
            string browseName = StatusCodes.GetBrowseName(StatusCodes.Good);
            Assert.AreEqual("Good", browseName);
        }

        /// <summary>
        /// Test GetBrowseName for Bad status code.
        /// </summary>
        [Test]
        public void GetBrowseName_BadStatusCode_ReturnsBad()
        {
            string browseName = StatusCodes.GetBrowseName(StatusCodes.Bad);
            Assert.AreEqual("Bad", browseName);
        }

        /// <summary>
        /// Test GetBrowseName for invalid status code ID returns empty string.
        /// </summary>
        [Test]
        public void GetBrowseName_InvalidStatusCodeId_ReturnsEmptyString()
        {
            string browseName = StatusCodes.GetBrowseName(0x12345678);
            Assert.AreEqual(string.Empty, browseName);
        }

        /// <summary>
        /// Test GetIdentifier for standard status code names.
        /// </summary>
        [Test]
        public void GetIdentifier_StandardStatusCodes_ReturnsValidIds()
        {
            // Test a few standard status code names - note that "Good" has id 0
            var testCases = new Dictionary<string, uint>
            {
                { "Good", StatusCodes.Good },
                { "Bad", StatusCodes.Bad },
                { "Uncertain", StatusCodes.Uncertain },
                { "BadNodeIdUnknown", StatusCodes.BadNodeIdUnknown },
                { "BadAttributeIdInvalid", StatusCodes.BadAttributeIdInvalid },
                { "BadIndexRangeInvalid", StatusCodes.BadIndexRangeInvalid },
                { "BadTypeMismatch", StatusCodes.BadTypeMismatch },
                { "GoodResultsMayBeIncomplete", StatusCodes.GoodResultsMayBeIncomplete },
                { "UncertainReferenceOutOfServer", StatusCodes.UncertainReferenceOutOfServer }
            };

            foreach (var testCase in testCases)
            {
                uint id = StatusCodes.GetIdentifier(testCase.Key);
                Assert.AreEqual(testCase.Value, id);
            }
        }

        /// <summary>
        /// Test GetIdentifier for Good status code name.
        /// </summary>
        [Test]
        public void GetIdentifier_GoodName_ReturnsGoodId()
        {
            uint id = StatusCodes.GetIdentifier("Good");
            Assert.AreEqual(StatusCodes.Good, id);
        }

        /// <summary>
        /// Test GetIdentifier for invalid name returns 0.
        /// </summary>
        [Test]
        public void GetIdentifier_InvalidName_ReturnsZero()
        {
            uint id = StatusCodes.GetIdentifier("InvalidStatusCodeName");
            Assert.AreEqual(0, id);
        }

        /// <summary>
        /// Test that GetBrowseName and GetIdentifier are inverse operations.
        /// </summary>
        [Test]
        public void GetBrowseName_GetIdentifier_AreInverseOperations()
        {
            uint[] statusCodeIds = {
                StatusCodes.Good, StatusCodes.Bad, StatusCodes.Uncertain,
                StatusCodes.BadNodeIdUnknown, StatusCodes.BadTypeMismatch
            };

            foreach (uint id in statusCodeIds)
            {
                string browseName = StatusCodes.GetBrowseName(id);
                uint retrievedId = StatusCodes.GetIdentifier(browseName);
                Assert.AreEqual(id, retrievedId);
            }
        }

        /// <summary>
        /// Test GetUtf8BrowseName for standard status codes.
        /// </summary>
        [Test]
        public void GetUtf8BrowseName_StandardStatusCodes_ReturnsValidUtf8Names()
        {
            uint[] statusCodeIds = {
                StatusCodes.Good,
                StatusCodes.Bad,
                StatusCodes.Uncertain,
                StatusCodes.BadNodeIdUnknown
            };

            foreach (uint id in statusCodeIds)
            {
                byte[] utf8BrowseName = StatusCodes.GetUtf8BrowseName(id);
                Assert.IsNotNull(utf8BrowseName);
                Assert.Greater(utf8BrowseName.Length, 0);
            }
        }

        /// <summary>
        /// Test GetUtf8BrowseName returns same as UTF8 encoding of GetBrowseName.
        /// </summary>
        [Test]
        public void GetUtf8BrowseName_MatchesUtf8EncodedGetBrowseName()
        {
            uint[] statusCodeIds = {
                StatusCodes.Good,
                StatusCodes.Bad,
                StatusCodes.BadNodeIdUnknown
            };

            foreach (uint id in statusCodeIds)
            {
                string browseName = StatusCodes.GetBrowseName(id);
                byte[] utf8BrowseName = StatusCodes.GetUtf8BrowseName(id);
                byte[] expectedUtf8 = System.Text.Encoding.UTF8.GetBytes(browseName);
                
                Assert.AreEqual(expectedUtf8, utf8BrowseName);
            }
        }
    }
}
