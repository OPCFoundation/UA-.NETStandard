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

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    /// <summary>
    /// Tests for <see cref="ServiceResultExtensions"/>.
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ServiceResultExtensionsTests
    {
        /// <summary>
        /// ToLongString for a basic ServiceResult with only a status code
        /// includes the status code representation.
        /// </summary>
        [Test]
        public void ToLongStringBasicResultContainsStatusCode()
        {
            var result = new ServiceResult(StatusCodes.Bad);
            string text = result.ToLongString();
            Assert.That(text, Is.Not.Null);
            Assert.That(text, Does.Contain("Id:"));
        }

        /// <summary>
        /// ToLongString for a ServiceResult with a symbolic ID
        /// includes the symbolic ID in the output.
        /// </summary>
        [Test]
        public void ToLongStringWithSymbolicIdContainsSymbolicId()
        {
            var result = new ServiceResult(
                namespaceUri: "http://opcfoundation.org/UA/",
                code: StatusCodes.BadNodeIdUnknown,
                localizedText: LocalizedText.Null,
                additionalInfo: null,
                innerResult: (ServiceResult)null);
            string text = result.ToLongString();
            Assert.That(text, Does.Contain("SymbolicId:"));
            Assert.That(text, Does.Contain("BadNodeIdUnknown"));
        }

        /// <summary>
        /// ToLongString for a ServiceResult with a localized text description
        /// includes the description in the output.
        /// </summary>
        [Test]
        public void ToLongStringWithLocalizedTextContainsDescription()
        {
            var result = new ServiceResult(
                namespaceUri: "http://opcfoundation.org/UA/",
                code: StatusCodes.BadNodeIdUnknown,
                localizedText: new LocalizedText("en", "The node id is unknown."),
                additionalInfo: null,
                innerResult: (ServiceResult)null);
            string text = result.ToLongString();
            Assert.That(text, Does.Contain("Description:"));
            Assert.That(text, Does.Contain("The node id is unknown."));
        }

        /// <summary>
        /// ToLongString for a ServiceResult with additional info
        /// includes the additional info in the output.
        /// </summary>
        [Test]
        public void ToLongStringWithAdditionalInfoContainsAdditionalInfo()
        {
            var result = new ServiceResult(
                namespaceUri: "http://opcfoundation.org/UA/",
                code: StatusCodes.BadNodeIdUnknown,
                localizedText: LocalizedText.Null,
                additionalInfo: "Additional diagnostic information.",
                innerResult: (ServiceResult)null);
            string text = result.ToLongString();
            Assert.That(text, Does.Contain("Additional diagnostic information."));
        }

        /// <summary>
        /// ToLongString for a ServiceResult with an inner result
        /// includes the inner result's details separated by "===".
        /// </summary>
        [Test]
        public void ToLongStringWithInnerResultContainsInnerResult()
        {
            var innerResult = new ServiceResult(StatusCodes.BadTimeout);
            var result = new ServiceResult(
                namespaceUri: "http://opcfoundation.org/UA/",
                code: StatusCodes.Bad,
                localizedText: LocalizedText.Null,
                additionalInfo: null,
                innerResult: innerResult);
            string text = result.ToLongString();
            Assert.That(text, Does.Contain("==="));
        }

        /// <summary>
        /// ToLongString for a ServiceResultException includes the exception message.
        /// </summary>
        [Test]
        public void ToLongStringExceptionContainsMessage()
        {
            var exception = new ServiceResultException(StatusCodes.BadTimeout, "Connection timed out.");
            string text = exception.ToLongString();
            Assert.That(text, Is.Not.Null);
            Assert.That(text, Does.Contain("Connection timed out."));
        }

        /// <summary>
        /// ToLongString for a ServiceResultException also includes the underlying
        /// ServiceResult code information.
        /// </summary>
        [Test]
        public void ToLongStringExceptionContainsResultCode()
        {
            var exception = new ServiceResultException(StatusCodes.BadTimeout);
            string text = exception.ToLongString();
            Assert.That(text, Is.Not.Null);
            Assert.That(text, Does.Contain("Id:"));
        }

        /// <summary>
        /// ToLongString for a good status code result produces a string that
        /// starts with "Id:".
        /// </summary>
        [Test]
        public void ToLongStringGoodResultStartsWithId()
        {
            var result = new ServiceResult(StatusCodes.Good);
            string text = result.ToLongString();
            Assert.That(text, Does.StartWith("Id:"));
        }

        /// <summary>
        /// ToLongString result with no localized text and no additional info
        /// does not contain "Description:" or nested result separators.
        /// </summary>
        [Test]
        public void ToLongStringMinimalResultContainsOnlyId()
        {
            var result = new ServiceResult(
                namespaceUri: null,
                code: new StatusCode(StatusCodes.Good.Code),
                localizedText: LocalizedText.Null,
                additionalInfo: null,
                innerResult: (ServiceResult)null);
            string text = result.ToLongString();
            Assert.That(text, Does.Not.Contain("Description:"));
            Assert.That(text, Does.Not.Contain("==="));
        }
    }
}
