#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;

namespace Opc.Ua.Client.Sessions
{
    [TestFixture]
    public sealed class ResultSetTests
    {
        [Test]
        public void ResultSetConstructorShouldInitializeProperties()
        {
            // Arrange
            var results = new List<int> { 1, 2, 3 };
            var errors = new List<ServiceResult> { ServiceResult.Good };

            // Act
            var resultSet = new ResultSet<int>(results, errors);

            // Assert
            Assert.That(resultSet.Results, Is.EqualTo(results));
            Assert.That(resultSet.Errors, Is.EqualTo(errors));
        }

        [Test]
        public void ResultSetEmptyShouldReturnEmptyResultSet()
        {
            // Act
            var emptyResultSet = ResultSet.Empty<int>();

            // Assert
            Assert.That(emptyResultSet.Results, Is.Empty);
            Assert.That(emptyResultSet.Errors, Is.Empty);
        }
    }
}
#endif
