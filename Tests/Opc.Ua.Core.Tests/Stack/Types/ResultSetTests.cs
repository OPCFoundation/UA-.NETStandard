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
            var results = new List<int> { 1, 2, 3 };
            var errors = new List<ServiceResult> { ServiceResult.Good };

            // Act
            var resultSet = new ResultSet<int>(results, errors);

            // Assert
            Assert.That(resultSet.Results, Is.EquivalentTo(results));
            Assert.That(resultSet.Errors, Is.EquivalentTo(errors));
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
            Assert.That(resultSet.Results, Is.EquivalentTo(results));
            Assert.That(resultSet.Errors, Is.EquivalentTo(errors));
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
            IReadOnlyList<int> results = [1, 2, 3];

            // Act
            var resultSet = ResultSet.From(results);

            // Assert
            Assert.That(resultSet.Results, Is.EquivalentTo(results));
            Assert.That(resultSet.Errors.Count, Is.EqualTo(3));
            Assert.That(resultSet.Errors, Is.All.EqualTo(ServiceResult.Good));
        }

        [Test]
        public void ResultSetFromShouldInitializePropertiesWithEnumerableWithoutErrorList()
        {
            // Arrange
            IEnumerable<int> results = [1, 2, 3];

            // Act
            var resultSet = ResultSet.From(results);

            // Assert
            Assert.That(resultSet.Results, Is.EquivalentTo(results));
            Assert.That(resultSet.Errors.Count, Is.EqualTo(3));
            Assert.That(resultSet.Errors, Is.All.EqualTo(ServiceResult.Good));
        }

        [Test]
        public void ResultSetEmptyShouldReturnAlwaysSameListReferences()
        {
            // Act
            ResultSet<int> emptyResultSet1 = ResultSet<int>.Empty;
            ResultSet<int> emptyResultSet2 = ResultSet<int>.Empty;

            // Assert
            Assert.That(emptyResultSet2.Results, Is.SameAs(emptyResultSet1.Results));
            Assert.That(emptyResultSet2.Errors, Is.SameAs(emptyResultSet1.Errors));
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
            Assert.That(resultSet2.Results, Is.SameAs(resultSet1.Results));
            Assert.That(resultSet2.Errors, Is.SameAs(resultSet1.Errors));
        }
    }
}
