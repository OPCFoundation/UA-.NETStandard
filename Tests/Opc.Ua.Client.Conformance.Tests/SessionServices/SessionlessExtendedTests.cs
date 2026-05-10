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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Conformance.Tests
{
    [TestFixture]
    [Category("Conformance")]
    [Category("SessionlessExtended")]
    public class SessionlessExtendedTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "Err-002")]
        public async Task GetEndpointsReturnsServerCertAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await cl.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            bool has = false;
            for (int i = 0; i < eps.Count; i++)
            {
                if (eps[i].ServerCertificate.Length > 0)
                {
                    has = true;
                    break;
                }
            }
            Assert.That(has, Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "013")]
        public async Task GetEndpointsReturnsSameAppUriAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await cl.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            string first = eps[0].Server.ApplicationUri;
            for (int i = 1; i < eps.Count; i++)
            {
                Assert.That(eps[i].Server.ApplicationUri, Is.EqualTo(first));
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task GetEndpointsHasSecurityModeNoneAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await cl.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            bool has = false;
            for (int i = 0; i < eps.Count; i++)
            {
                if (eps[i].SecurityMode == MessageSecurityMode.None)
                {
                    has = true;
                    break;
                }
            }
            Assert.That(has, Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task GetEndpointsHasSignAndEncryptAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await cl.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            bool has = false;
            for (int i = 0; i < eps.Count; i++)
            {
                if (eps[i].SecurityMode == MessageSecurityMode.SignAndEncrypt)
                {
                    has = true;
                    break;
                }
            }
            if (!has)
            {
                Assert.Fail("No SignAndEncrypt endpoints.");
            }
            Assert.That(has, Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Find Servers Self")]
        [Property("Tag", "001")]
        public async Task FindServersReturnsDiscoveryUrlsAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> svrs = await cl.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            for (int i = 0; i < svrs.Count; i++)
            {
                Assert.That(svrs[i].DiscoveryUrls.Count, Is.GreaterThan(0));
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "007")]

        public async Task ConcurrentGetEndpointsAsync()
        {
            var tasks = new Task<ArrayOf<EndpointDescription>>[5];
            for (
            int i = 0;
            i < 5;
            i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    var ec = EndpointConfiguration.Create(
                        ClientFixture.Config);
                    using DiscoveryClient c = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
                    return await c.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
                });
            }
            ArrayOf<EndpointDescription>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
            foreach (ArrayOf<EndpointDescription> r in results)
            {
                Assert.That(r.Count, Is.GreaterThan(0));
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Find Servers Self")]
        [Property("Tag", "004")]

        public async Task ConcurrentFindServersAsync()
        {
            var tasks = new Task<ArrayOf<ApplicationDescription>>[5];
            for (
            int i = 0;
            i < 5;
            i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    var ec = EndpointConfiguration.Create(
                        ClientFixture.Config);
                    using DiscoveryClient c = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
                    return await c.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
                });
            }
            ArrayOf<ApplicationDescription>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
            foreach (ArrayOf<ApplicationDescription> r in results)
            {
                Assert.That(r.Count, Is.GreaterThan(0));
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task EndpointSecurityPolicyUriIsValidAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await cl.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            for (int i = 0; i < eps.Count; i++)
            {
                Assert.That(eps[i].SecurityPolicyUri, Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task EndpointTransportProfileUriIsValidAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await cl.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            for (int i = 0; i < eps.Count; i++)
            {
                Assert.That(eps[i].TransportProfileUri, Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task EndpointUserTokenPoliciesExistAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await cl.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            bool anyHasTokens = false;
            for (int i = 0; i < eps.Count; i++)
            {
                if (eps[i].UserIdentityTokens.Count > 0)
                {
                    anyHasTokens = true;
                }
            }
            if (!anyHasTokens)
            {
                Assert.Fail("No endpoints advertise user identity tokens.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task EndpointNoneHasAnonymousTokenAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await cl.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            for (int i = 0; i < eps.Count; i++)
            {
                if (eps[i].SecurityMode != MessageSecurityMode.None)
                {
                    continue;
                }
                bool a = false;
                for (int j = 0; j < eps[i].UserIdentityTokens.Count; j++)
                {
                    if (eps[i].UserIdentityTokens[j].TokenType == UserTokenType.Anonymous)
                    {
                        a = true;
                        break;
                    }
                }
                Assert.That(a, Is.True);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Find Servers Self")]
        [Property("Tag", "001")]
        public async Task FindServersAppNameNotEmptyAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> svrs = await cl.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            for (int i = 0; i < svrs.Count; i++)
            {
                Assert.That(svrs[i].ApplicationName.Text, Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Find Servers Self")]
        [Property("Tag", "001")]
        public async Task FindServersProductUriNotEmptyAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> svrs = await cl.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            for (int i = 0; i < svrs.Count; i++)
            {
                Assert.That(svrs[i].ProductUri, Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "012")]
        public async Task GetEndpointsRepeatedConsistentAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> e1 = await cl.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> e2 = await cl.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(e1.Count, Is.EqualTo(e2.Count));
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Find Servers Self")]
        [Property("Tag", "004")]
        public async Task FindServersRepeatedConsistentAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> s1 = await cl.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> s2 = await cl.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(s1.Count, Is.EqualTo(s2.Count));
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task GetEndpointsAfterFindServersAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            await cl.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await cl.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(eps.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Find Servers Self")]
        [Property("Tag", "001")]
        public async Task FindServersAfterGetEndpointsAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            await cl.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> svrs = await cl.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(svrs.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "007")]

        public async Task SessionlessClientNoLeakAsync()
        {
            for (
            int i = 0;
            i < 10;
            i++)
            {
                var ec = EndpointConfiguration.Create(ClientFixture.Config);
                using DiscoveryClient c = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
                await c.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            }
            Assert.Pass("10 cycles OK.");
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "013")]
        public async Task GetEndpointsReturnsApplicationUriAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await cl.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            for (int i = 0; i < eps.Count; i++)
            {
                Assert.That(eps[i].Server.ApplicationUri, Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task SessionlessDiscoveryNoAuthAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await cl.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(eps.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task EndpointSecurityLevelIsSetAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await cl.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            for (int i = 0; i < eps.Count; i++)
            {
                Assert.That(eps[i].SecurityLevel, Is.GreaterThanOrEqualTo((byte)0));
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "001")]
        public async Task GetEndpointsServerNameNotEmptyAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await cl.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            for (int i = 0; i < eps.Count; i++)
            {
                Assert.That(eps[i].Server.ApplicationName.Text, Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Discovery Get Endpoints")]
        [Property("Tag", "013")]
        public async Task FindServersSameAppUriAsEndpointsAsync()
        {
            var ec = EndpointConfiguration.Create(
            ClientFixture.Config);
            using DiscoveryClient cl = await DiscoveryClient.CreateAsync(ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await cl.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> svrs = await cl.FindServersAsync(default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(svrs[0].ApplicationUri, Is.EqualTo(eps[0].Server.ApplicationUri));
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "Err-004")]

        public async Task ServerHandlesEmptyReadListAsync()
        {
            try
            {
                ReadResponse resp = await Session.ReadAsync(
            null,
            0,
            TimestampsToReturn.Both,
            Array.Empty<ReadValueId>().ToArrayOf(),
            CancellationToken.None).ConfigureAwait(false);
                Assert.That(
                    StatusCode.IsGood(resp.ResponseHeader.ServiceResult) || resp.ResponseHeader.ServiceResult.Code == StatusCodes.BadNothingToDo,
                    Is.True);
                Assert.That(resp.Results.Count, Is.Zero);
            }
            catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes.BadNothingToDo)
            { /* BadNothingToDo is valid per spec for empty read list */
            }
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "004")]

        public async Task ServerTimestampIsRecentAsync()
        {
            ReadResponse resp = await Session.ReadAsync(
            null,
            0,
            TimestampsToReturn.Both,
            new ReadValueId[] { new() { NodeId = ToNodeId(Constants.ScalarStaticInt32), AttributeId = Attributes.Value } }.ToArrayOf(),
            CancellationToken.None).ConfigureAwait(false);
            Assert.That((DateTime.UtcNow - resp.ResponseHeader.Timestamp).TotalSeconds, Is.LessThan(60));
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "004")]

        public async Task ServiceResultGoodForValidReadAsync()
        {
            ReadResponse resp = await Session.ReadAsync(
            null,
            0,
            TimestampsToReturn.Both,
            new ReadValueId[] { new() { NodeId = ToNodeId(Constants.ScalarStaticInt32), AttributeId = Attributes.Value } }.ToArrayOf(),
            CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "Err-004")]

        public async Task WriteInvalidNodeIdReturnsBadAsync()
        {
            WriteResponse resp = await Session.WriteAsync(null, new WriteValue[] {
            new() {
                NodeId = Constants.InvalidNodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(0)) } }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(resp.Results[0].Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "Err-004")]

        public async Task ReadInvalidNodeIdReturnsBadAsync()
        {
            ReadResponse resp = await Session.ReadAsync(
            null,
            0,
            TimestampsToReturn.Both,
            new ReadValueId[] { new() { NodeId = Constants.InvalidNodeId, AttributeId = Attributes.Value } }.ToArrayOf(),
            CancellationToken.None).ConfigureAwait(false);
            Assert.That(resp.Results[0].StatusCode.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        [Property("ConformanceUnit", "Session Base")]
        [Property("Tag", "Err-004")]
        public async Task BrowseInvalidNodeIdReturnsBadAsync()
        {
            BrowseResponse resp = await Session.BrowseAsync(null, null, 0, new BrowseDescription[] { new() {
                NodeId = Constants.InvalidNodeId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All } }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(resp.Results[0].StatusCode.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }
    }
}
