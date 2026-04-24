// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Sessions
{
    using System.Collections.Generic;
    using Xunit;

    public sealed class ResultSetTests
    {
        [Fact]
        public void ResultSetConstructorShouldInitializeProperties()
        {
            // Arrange
            var results = new List<int> { 1, 2, 3 };
            var errors = new List<ServiceResult> { ServiceResult.Good };

            // Act
            var resultSet = new ResultSet<int>(results, errors);

            // Assert
            Assert.Equal(results, resultSet.Results);
            Assert.Equal(errors, resultSet.Errors);
        }

        [Fact]
        public void ResultSetEmptyShouldReturnEmptyResultSet()
        {
            // Act
            var emptyResultSet = ResultSet.Empty<int>();

            // Assert
            Assert.Empty(emptyResultSet.Results);
            Assert.Empty(emptyResultSet.Errors);
        }
    }
}
