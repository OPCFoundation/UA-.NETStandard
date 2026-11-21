// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
                string browseName = id.SymbolicId!;
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
            string browseName = StatusCodes.Good.SymbolicId!;
            Assert.That(browseName, Is.EqualTo("Good"));
        }

        /// <summary>
        /// Test GetBrowseName for Bad status code.
        /// </summary>
        [Test]
        public void StatusCode_SymbolicId_BadStatusCode_ReturnsBad()
        {
            string browseName = StatusCodes.Bad.SymbolicId!;
            Assert.That(browseName, Is.EqualTo("Bad"));
        }

        /// <summary>
        /// Test GetBrowseName for invalid status code ID returns empty string.
        /// </summary>
        [Test]
        public void StatusCode_SymbolicId_InvalidStatusCodeId_ReturnsEmptyString()
        {
            string? browseName = new StatusCode(0x12345678).SymbolicId;
            Assert.That(browseName, Is.Null);
        }
    }
}
