/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Types
{
    /// <summary>
    /// Tests for the result set type
    /// </summary>
    [TestFixture]
    [Category("ResultSet")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ResultSetTests
    {
        [Test]
        public void ResultSetConstructorShouldInitializeProperties()
        {
            // Arrange
            ArrayOf<int> results = [1, 2, 3];
            ArrayOf<ServiceResult> errors = [ServiceResult.Good];

            // Act
            var resultSet = new ResultSet<int>(results, errors);

            // Assert
            Assert.That(resultSet.Results, Is.EqualTo(results));
            Assert.That(resultSet.Errors, Is.EqualTo(errors));
        }

        [Test]
        public void ResultSetFromShouldInitializeProperties()
        {
            // Arrange
            IEnumerable<int> results = [1, 2, 3];
            var errors = new List<ServiceResult> { ServiceResult.Good };

            // Act
            var resultSet = ResultSet.From(results, errors);

            // Assert
            Assert.That(resultSet.Results, Is.EqualTo(ArrayOf.Wrapped(1, 2, 3)));
            Assert.That(resultSet.Errors, Is.EqualTo(
                ArrayOf.Wrapped(ServiceResult.Good)));
        }

        [Test]
        public void ResultSetEmptyShouldReturnEmptyResultSet()
        {
            // Act
            ResultSet<int> emptyResultSet = ResultSet<int>.Empty;

            // Assert
            Assert.That(emptyResultSet.Results, Is.Empty);
            Assert.That(emptyResultSet.Errors, Is.Empty);
        }

        [Test]
        public void ResultSetFromShouldInitializePropertiesWithoutErrorList()
        {
            // Arrange
            ArrayOf<int> results = [1, 2, 3];

            // Act
            var resultSet = ResultSet.From(results);

            // Assert
            Assert.That(resultSet.Results, Is.EqualTo(results));
            Assert.That(resultSet.Errors.Count, Is.EqualTo(3));
            Assert.That(resultSet.Errors.ToList(), Is.All.EqualTo(ServiceResult.Good));
        }

        [Test]
        public void ResultSetFromShouldInitializePropertiesWithEnumerableWithoutErrorList()
        {
            // Arrange
            IEnumerable<int> results = [1, 2, 3];

            // Act
            var resultSet = ResultSet.From(results);

            // Assert
            Assert.That(resultSet.Results, Is.EqualTo(ArrayOf.Wrapped(1, 2, 3)));
            Assert.That(resultSet.Errors.Count, Is.EqualTo(3));
            Assert.That(resultSet.Errors.ToList(), Is.All.EqualTo(ServiceResult.Good));
        }

        [Test]
        public void ResultSetEmptyShouldReturnAlwaysSameListReferences()
        {
            // Act
            ResultSet<int> emptyResultSet1 = ResultSet<int>.Empty;
            ResultSet<int> emptyResultSet2 = ResultSet<int>.Empty;

            // Assert
            Assert.That(emptyResultSet2.Results, Is.EqualTo(emptyResultSet1.Results));
            Assert.That(emptyResultSet2.Errors, Is.EqualTo(emptyResultSet1.Errors));
        }

        [Test]
        public void ResultSetCopiedShouldReturnAlwaysSameListReferences()
        {
            // Arrange
            var results = new List<int> { 1, 2, 3 };
            var errors = new List<ServiceResult> { ServiceResult.Good };

            // Act
            var resultSet1 = ResultSet.From(results, errors);
            ResultSet<int> resultSet2 = resultSet1;

            // Assert
            Assert.That(resultSet2.Results, Is.EqualTo(resultSet1.Results));
            Assert.That(resultSet2.Errors, Is.EqualTo(resultSet1.Errors));
        }
    }
}
