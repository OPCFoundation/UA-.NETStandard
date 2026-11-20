// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
