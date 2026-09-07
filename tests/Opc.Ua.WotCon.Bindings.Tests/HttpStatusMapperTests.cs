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

using System.Net;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings.Http;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Unit tests for the HTTP status code to OPC UA StatusCode mapper.
    /// </summary>
    [TestFixture]
    public sealed class HttpStatusMapperTests
    {
        [Test]
        public void OkMapsToGood()
        {
            Assert.That(HttpStatusMapper.Map(HttpStatusCode.OK),
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void CreatedMapsToGood()
        {
            Assert.That(HttpStatusMapper.Map(HttpStatusCode.Created),
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void NoContentMapsToGood()
        {
            Assert.That(HttpStatusMapper.Map(HttpStatusCode.NoContent),
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void PartialContentMapsToGood()
        {
            Assert.That(HttpStatusMapper.Map(HttpStatusCode.PartialContent),
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void BadRequestMapsToBadInvalidArgument()
        {
            Assert.That(HttpStatusMapper.Map(HttpStatusCode.BadRequest),
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void UnauthorizedMapsToBadUserAccessDenied()
        {
            Assert.That(HttpStatusMapper.Map(HttpStatusCode.Unauthorized),
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void ForbiddenMapsToBadUserAccessDenied()
        {
            Assert.That(HttpStatusMapper.Map(HttpStatusCode.Forbidden),
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void NotFoundMapsToBadNodeIdUnknown()
        {
            Assert.That(HttpStatusMapper.Map(HttpStatusCode.NotFound),
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void MethodNotAllowedMapsToBadNotSupported()
        {
            Assert.That(HttpStatusMapper.Map(HttpStatusCode.MethodNotAllowed),
                Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public void RequestTimeoutMapsToBadTimeout()
        {
            Assert.That(HttpStatusMapper.Map(HttpStatusCode.RequestTimeout),
                Is.EqualTo(StatusCodes.BadTimeout));
        }

        [Test]
        public void ConflictMapsToBadInvalidState()
        {
            Assert.That(HttpStatusMapper.Map(HttpStatusCode.Conflict),
                Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void NotImplementedMapsToBadNotImplemented()
        {
            Assert.That(HttpStatusMapper.Map(HttpStatusCode.NotImplemented),
                Is.EqualTo(StatusCodes.BadNotImplemented));
        }

        [Test]
        public void ServiceUnavailableMapsToBadServerHalted()
        {
            Assert.That(HttpStatusMapper.Map(HttpStatusCode.ServiceUnavailable),
                Is.EqualTo(StatusCodes.BadServerHalted));
        }

        [Test]
        public void GatewayTimeoutMapsToBadTimeout()
        {
            Assert.That(HttpStatusMapper.Map(HttpStatusCode.GatewayTimeout),
                Is.EqualTo(StatusCodes.BadTimeout));
        }

        [Test]
        public void InternalServerErrorMapsToBadInternalError()
        {
            Assert.That(HttpStatusMapper.Map(HttpStatusCode.InternalServerError),
                Is.EqualTo(StatusCodes.BadInternalError));
        }

        [Test]
        public void GenericFiveHundredXxMapsToBadInternalError()
        {
            Assert.That(HttpStatusMapper.Map((HttpStatusCode)599),
                Is.EqualTo(StatusCodes.BadInternalError));
        }

        [Test]
        public void GoneMapsToBadUnexpectedError()
        {
            Assert.That(HttpStatusMapper.Map(HttpStatusCode.Gone),
                Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        public void RedirectMapsToBadUnexpectedError()
        {
            Assert.That(HttpStatusMapper.Map(HttpStatusCode.MovedPermanently),
                Is.EqualTo(StatusCodes.BadUnexpectedError));
        }
    }
}
