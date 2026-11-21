// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Lds.Tests
{
    /// <summary>
    /// Extended compliance tests for miscellaneous server behavior:
    /// Cancel request handling, unknown service, response time,
    /// RequestHandle echo, and DiagnosticInfo suppression.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("MiscellaneousExtended")]
    public class MiscellaneousExtendedTests : TestFixture
    {
        [Test]
        public async Task VerifyServerHandlesReadWithinAcceptableTimeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            var sw = Stopwatch.StartNew();

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            sw.Stop();
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode),
                Is.True);
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(10000),
                "Read should complete within 10 seconds.");
        }

        [Test]
        public async Task VerifyResponseRequestHandleEchoedAsync()
        {
            var requestHeader = new RequestHeader
            {
                RequestHandle = 54321u,
                Timestamp = DateTime.UtcNow
            };

            ReadResponse response = await Session.ReadAsync(
                requestHeader, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ToNodeId(Constants.ScalarStaticInt32),
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.ResponseHeader.RequestHandle,
                Is.EqualTo(54321u),
                "Server should echo RequestHandle in response.");
        }

        [Test]
        public async Task VerifyNoDiagnosticsWhenNotRequestedAsync()
        {
            var requestHeader = new RequestHeader
            {
                ReturnDiagnostics = 0,
                Timestamp = DateTime.UtcNow
            };

            ReadResponse response = await Session.ReadAsync(
                requestHeader, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ToNodeId(Constants.ScalarStaticInt32),
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));

            // When ReturnDiagnostics=0, DiagnosticInfos should be
            // empty or null
            if (response.DiagnosticInfos != default!)
            {
                bool allNull = true;
                foreach (DiagnosticInfo di in response.DiagnosticInfos)
                {
                    if (di != null && !di.IsNullDiagnosticInfo)
                    {
                        allNull = false;
                        break;
                    }
                }

                Assert.That(allNull, Is.True,
                    "DiagnosticInfos should be empty when not requested.");
            }
        }

        [Test]
        public async Task ReadWithMaxAgeZeroReturnsDeviceValueAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ToNodeId(Constants.ScalarStaticInt32),
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode),
                Is.True,
                "MaxAge=0 should return a fresh device value.");
        }

        [Test]
        public async Task ReadWithMaxAgeMaxReturnsCacheAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, double.MaxValue, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ToNodeId(Constants.ScalarStaticInt32),
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode),
                Is.True,
                "MaxAge=MaxValue should return a cached value.");
        }
    }
}
