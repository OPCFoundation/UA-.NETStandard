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

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// The response header of a request that wrote no diagnostic strings is built with an
    /// empty string table of the thread; overrides of the response hook must not leak
    /// strings into later responses through it.
    /// </summary>
    [TestFixture]
    [Category("StandardServer")]
    [Parallelizable]
    public class StandardServerResponseStringTableTests
    {
        /// <summary>
        /// A request without diagnostic strings gets an empty, not a null, string table.
        /// </summary>
        [Test]
        public void ResponseWithoutStringsHasEmptyStringTable()
        {
            using var server = new HookServer();

            ResponseHeader header = server.CreateResponse(new RequestHeader { RequestHandle = 7 }, NewContext());

            Assert.That(header.RequestHandle, Is.EqualTo(7u));
            Assert.That(header.StringTable.IsNull, Is.False);
            Assert.That(header.StringTable.Count, Is.Zero);
        }

        /// <summary>
        /// An override that adds strings to the table and then throws must not hand those
        /// strings to the next response created on the thread.
        /// </summary>
        [Test]
        public void ThrowingOverrideDoesNotLeakStringsIntoTheNextResponse()
        {
            using var server = new HookServer();
            server.Hook = (header, strings) =>
            {
                strings.Append("leaked");
                throw new InvalidOperationException("hook failed");
            };

            Assert.That(
                () => server.CreateResponse(new RequestHeader(), NewContext()),
                Throws.InvalidOperationException);

            server.Hook = null;
            ResponseHeader next = server.CreateResponse(new RequestHeader(), NewContext());
            Assert.That(next.StringTable.Count, Is.Zero);
        }

        /// <summary>
        /// Strings an override adds go into its own response only.
        /// </summary>
        [Test]
        public void StringsAddedByOverrideStayInItsResponse()
        {
            using var server = new HookServer();
            server.Hook = (header, strings) =>
            {
                strings.Append("mine");
                return server.BaseCreateResponse(header, strings);
            };

            ResponseHeader first = server.CreateResponse(new RequestHeader(), NewContext());
            Assert.That(first.StringTable.Count, Is.EqualTo(1));
            Assert.That(first.StringTable[0], Is.EqualTo("mine"));

            server.Hook = null;
            ResponseHeader next = server.CreateResponse(new RequestHeader(), NewContext());
            Assert.That(next.StringTable.Count, Is.Zero);
        }

        /// <summary>
        /// A response created while the hook runs (re-entrantly) gets a table of its own.
        /// </summary>
        [Test]
        public void ReentrantResponseGetsItsOwnTable()
        {
            using var server = new HookServer();
            var tables = new List<StringTable>();
            server.Hook = (header, strings) =>
            {
                tables.Add(strings);
                if (tables.Count == 1)
                {
                    strings.Append("outer");
                    ResponseHeader inner = server.CreateResponse(new RequestHeader(), NewContext());
                    Assert.That(inner.StringTable.Count, Is.Zero);
                }
                return server.BaseCreateResponse(header, strings);
            };

            ResponseHeader outer = server.CreateResponse(new RequestHeader(), NewContext());

            Assert.That(tables, Has.Count.EqualTo(2));
            Assert.That(tables[1], Is.Not.SameAs(tables[0]));
            Assert.That(outer.StringTable.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// A request that wrote diagnostic strings gets them in its response.
        /// </summary>
        [Test]
        public void ResponseCarriesTheStringsOfTheRequest()
        {
            using var server = new HookServer();
            OperationContext context = NewContext();
            context.StringTable.Append("diagnostic");

            ResponseHeader header = server.CreateResponse(new RequestHeader(), context);

            Assert.That(header.StringTable.Count, Is.EqualTo(1));
            Assert.That(header.StringTable[0], Is.EqualTo("diagnostic"));
        }

        /// <summary>
        /// The default header a service offers is filled and returned instead of a new one,
        /// with every field the response header had before.
        /// </summary>
        [Test]
        public void OfferedHeaderIsFilledWithEveryField()
        {
            using var server = new HookServer();
            var offered = new ResponseHeader();
            OperationContext context = NewContext();
            context.StringTable.Append("diagnostic");
            DateTimeUtc before = DateTimeUtc.Now;

            ResponseHeader header = server.CreateResponse(new RequestHeader { RequestHandle = 9 }, context, offered);

            Assert.That(header, Is.SameAs(offered));
            Assert.That(header.RequestHandle, Is.EqualTo(9u));
            Assert.That(header.Timestamp, Is.GreaterThanOrEqualTo(before));
            Assert.That(header.Timestamp, Is.LessThanOrEqualTo(DateTimeUtc.Now));
            Assert.That(header.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(header.ServiceDiagnostics, Is.Null);
            Assert.That(header.StringTable.Count, Is.EqualTo(1));
            Assert.That(header.StringTable[0], Is.EqualTo("diagnostic"));
        }

        /// <summary>
        /// An override that does not call the base keeps returning its own header, the
        /// offered header stays untouched, and a later call does not get it.
        /// </summary>
        [Test]
        public void OverrideWithoutBaseKeepsItsOwnHeader()
        {
            using var server = new HookServer();
            var own = new ResponseHeader { RequestHandle = 5 };
            server.Hook = (header, strings) => own;
            var offered = new ResponseHeader();

            ResponseHeader result = server.CreateResponse(new RequestHeader { RequestHandle = 9 }, NewContext(), offered);

            Assert.That(result, Is.SameAs(own));
            Assert.That(offered.RequestHandle, Is.Zero);
            Assert.That(offered.Timestamp, Is.EqualTo(DateTimeUtc.MinValue));

            server.Hook = null;
            ResponseHeader next = server.CreateResponse(new RequestHeader { RequestHandle = 10 }, NewContext());
            Assert.That(next, Is.Not.SameAs(offered));
            Assert.That(next.RequestHandle, Is.EqualTo(10u));
        }

        /// <summary>
        /// An override that calls the base gets the offered header and may change it.
        /// </summary>
        [Test]
        public void OverrideCallingBaseFillsTheOfferedHeader()
        {
            using var server = new HookServer();
            server.Hook = (header, strings) =>
            {
                ResponseHeader created = server.BaseCreateResponse(header, strings);
                created.ServiceResult = StatusCodes.GoodCallAgain;
                return created;
            };
            var offered = new ResponseHeader();

            ResponseHeader result = server.CreateResponse(new RequestHeader { RequestHandle = 3 }, NewContext(), offered);

            Assert.That(result, Is.SameAs(offered));
            Assert.That(result.RequestHandle, Is.EqualTo(3u));
            Assert.That(result.ServiceResult, Is.EqualTo(StatusCodes.GoodCallAgain));
        }

        /// <summary>
        /// A response created while the hook runs (re-entrantly) never shares the header
        /// offered for the outer response.
        /// </summary>
        [Test]
        public void ReentrantResponseDoesNotShareTheOfferedHeader()
        {
            using var server = new HookServer();
            ResponseHeader? inner = null;
            server.Hook = (header, strings) =>
            {
                if (inner == null)
                {
                    server.Hook = null;
                    inner = server.CreateResponse(new RequestHeader { RequestHandle = 2 }, NewContext());
                    server.Hook = (h, s) => server.BaseCreateResponse(h, s);
                }
                return server.BaseCreateResponse(header, strings);
            };
            var offered = new ResponseHeader();

            ResponseHeader outer = server.CreateResponse(new RequestHeader { RequestHandle = 1 }, NewContext(), offered);

            Assert.That(inner, Is.Not.Null);
            Assert.That(inner, Is.Not.SameAs(outer));
            Assert.That(inner!.RequestHandle, Is.EqualTo(2u));
            Assert.That(outer.RequestHandle, Is.EqualTo(1u));
        }

        private static OperationContext NewContext()
        {
            return new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
        }

        /// <summary>
        /// A server whose response hook a test replaces.
        /// </summary>
        private sealed class HookServer : StandardServer
        {
            public HookServer()
                : base(NUnitTelemetryContext.Create())
            {
            }

            public Func<RequestHeader, StringTable, ResponseHeader>? Hook { get; set; }

            public ResponseHeader BaseCreateResponse(RequestHeader requestHeader, StringTable stringTable)
            {
                return base.CreateResponse(requestHeader, stringTable);
            }

            protected override ResponseHeader CreateResponse(RequestHeader requestHeader, StringTable stringTable)
            {
                Func<RequestHeader, StringTable, ResponseHeader>? hook = Hook;
                return hook != null ? hook(requestHeader, stringTable) : base.CreateResponse(requestHeader, stringTable);
            }
        }
    }
}
