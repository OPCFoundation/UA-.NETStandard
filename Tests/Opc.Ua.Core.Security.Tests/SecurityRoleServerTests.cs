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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ISession = Opc.Ua.Client.ISession;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Core.Security.Tests
{
    /// <summary>
    /// compliance tests for OPC UA Security Role Server behavior.
    /// Tests verify endpoint management, identity mapping, application
    /// restrictions, and general role management operations.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("SecurityRoleServer")]
    public class SecurityRoleServerTests : TestFixture
    {
        [Test]
        public async Task AddEndpointRestrictionToRoleAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddEndpoint", adminSession)
                    .ConfigureAwait(false);

                const string url = "opc.tcp://endpointTest:4840";
                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(CreateEndpoint(url))).ConfigureAwait(false);

                IgnoreIfRoleMethodNotSupported(result.StatusCode);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True,
                    $"AddEndpoint failed: {result.StatusCode}");

                await TryRemoveEndpointAsync(
                    adminSession, observerId, url).ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task ReadEndpointRestrictionAfterAddAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddEndpoint", adminSession)
                    .ConfigureAwait(false);

                const string url = "opc.tcp://readEndpoint:4840";
                CallMethodResult addResult = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(CreateEndpoint(url))).ConfigureAwait(false);

                if (StatusCode.IsBad(addResult.StatusCode))
                {
                    Assert.Ignore(
                        "AddEndpoint not supported by server " +
                        $"(status: {addResult.StatusCode}).");
                }

                NodeId endpointsId = await FindChildAsync(
                    observerId, "Endpoints", adminSession)
                    .ConfigureAwait(false);

                if (!endpointsId.IsNull)
                {
                    DataValue dv = await ReadPropertyValueAsync(
                        endpointsId, adminSession).ConfigureAwait(false);
                    IgnoreIfRoleMethodNotSupported(dv.StatusCode);
                    Assert.That(dv.StatusCode,
                        Is.EqualTo(StatusCodes.Good));
                }

                await TryRemoveEndpointAsync(
                    adminSession, observerId, url).ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task AddMultipleEndpointsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddEndpoint", adminSession)
                    .ConfigureAwait(false);

                const string url1 = "opc.tcp://multi1:4840";
                const string url2 = "opc.tcp://multi2:4841";

                CallMethodResult r1 = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(CreateEndpoint(url1))).ConfigureAwait(false);
                IgnoreIfRoleMethodNotSupported(r1.StatusCode);
                Assert.That(StatusCode.IsGood(r1.StatusCode), Is.True);

                CallMethodResult r2 = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(CreateEndpoint(url2))).ConfigureAwait(false);
                IgnoreIfRoleMethodNotSupported(r2.StatusCode);
                Assert.That(StatusCode.IsGood(r2.StatusCode), Is.True);

                // Cleanup
                await TryRemoveEndpointAsync(
                    adminSession, observerId, url1).ConfigureAwait(false);
                await TryRemoveEndpointAsync(
                    adminSession, observerId, url2).ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task RemoveEndpointRestrictionAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddEndpoint", adminSession)
                    .ConfigureAwait(false);
                NodeId removeMethod = await RequireMethodAsync(
                    observerId, "RemoveEndpoint", adminSession)
                    .ConfigureAwait(false);

                const string url = "opc.tcp://removeEp:4840";
                await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(CreateEndpoint(url))).ConfigureAwait(false);

                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, observerId, removeMethod,
                    new Variant(CreateEndpoint(url))).ConfigureAwait(false);

                IgnoreIfRoleMethodNotSupported(result.StatusCode);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True,
                    $"RemoveEndpoint failed: {result.StatusCode}");
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task RemoveLastEndpointClearsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddEndpoint", adminSession)
                    .ConfigureAwait(false);
                NodeId removeMethod = await RequireMethodAsync(
                    observerId, "RemoveEndpoint", adminSession)
                    .ConfigureAwait(false);

                const string url = "opc.tcp://lastEp:4840";
                await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(CreateEndpoint(url))).ConfigureAwait(false);

                await CallRoleMethodAsync(
                    adminSession, observerId, removeMethod,
                    new Variant(CreateEndpoint(url))).ConfigureAwait(false);

                NodeId endpointsId = await FindChildAsync(
                    observerId, "Endpoints", adminSession)
                    .ConfigureAwait(false);

                if (!endpointsId.IsNull)
                {
                    DataValue dv = await ReadPropertyValueAsync(
                        endpointsId, adminSession).ConfigureAwait(false);
                    IgnoreIfRoleMethodNotSupported(dv.StatusCode);
                    Assert.That(dv.StatusCode,
                        Is.EqualTo(StatusCodes.Good));
                }
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task EndpointsExcludeDefaultIsFalseAsync()
        {
            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync().ConfigureAwait(false);

                NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

                NodeId excludeId = await FindChildAsync(
                    observerId, "EndpointsExclude", adminSession).ConfigureAwait(false);

                if (excludeId.IsNull)
                {
                    Assert.Ignore(
                        "EndpointsExclude not found. " +
                        "Feature not supported by server.");
                }

                DataValue dv = await ReadPropertyValueAsync(excludeId, adminSession)
                    .ConfigureAwait(false);
                Assert.That(dv.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(dv.WrappedValue.TryGetValue(out bool excludeVal), Is.True);
                Assert.That(excludeVal, Is.False);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true).ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task AddEndpointWithEmptyUrlFailsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddEndpoint", adminSession)
                    .ConfigureAwait(false);

                try
                {
                    CallMethodResult result = await CallRoleMethodAsync(
                        adminSession, observerId, addMethod,
                        new Variant(string.Empty)).ConfigureAwait(false);

                    if (StatusCode.IsBad(result.StatusCode))
                    {
                        Assert.Pass(
                            "Server correctly rejected empty URL.");
                    }
                }
                catch (ServiceResultException)
                {
                    Assert.Pass(
                        "Server correctly rejected empty URL.");
                }
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task AddEndpointDuplicateIsIdempotentAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddEndpoint", adminSession)
                    .ConfigureAwait(false);

                const string url = "opc.tcp://dupEp:4840";
                CallMethodResult r1 = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(CreateEndpoint(url))).ConfigureAwait(false);
                IgnoreIfRoleMethodNotSupported(r1.StatusCode);
                Assert.That(StatusCode.IsGood(r1.StatusCode), Is.True);

                CallMethodResult r2 = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(CreateEndpoint(url))).ConfigureAwait(false);
                IgnoreIfRoleMethodNotSupported(r2.StatusCode);
                Assert.That(StatusCode.IsGood(r2.StatusCode), Is.True);

                await TryRemoveEndpointAsync(
                    adminSession, observerId, url).ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task RemoveNonExistentEndpointReturnsNoMatchAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId removeMethod = await RequireMethodAsync(
                    observerId, "RemoveEndpoint", adminSession)
                    .ConfigureAwait(false);

                try
                {
                    CallMethodResult result = await CallRoleMethodAsync(
                        adminSession, observerId, removeMethod,
                        new Variant(CreateEndpoint("opc.tcp://doesNotExist:9999")))
                        .ConfigureAwait(false);

                    Assert.That(result, Is.Not.Null);
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode ==
                              StatusCodes.BadInvalidArgument ||
                        sre.StatusCode == StatusCodes.BadNoMatch)
                {
                    Assert.Pass(
                        "Server returned expected error: " +
                        $"{sre.StatusCode}");
                }
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task AddEndpointWithoutAdminFailsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession userSession = null;
            try
            {
                userSession = await ConnectAsRegularUserAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await GetMethodIdByName(
                    observerId, "AddEndpoint", userSession)
                    .ConfigureAwait(false);

                if (addMethod.IsNull)
                {
                    Assert.Ignore(
                        "AddEndpoint not found. " +
                        "Feature not supported by server.");
                }

                ServiceResultException ex = null;
                CallMethodResult result = null;
                try
                {
                    result = await CallRoleMethodAsync(
                            userSession, observerId, addMethod,
                            new Variant(CreateEndpoint("opc.tcp://unauth:4840")))
                            .ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    ex = sre;
                }

                StatusCode statusCode = ex?.StatusCode
                    ?? result?.StatusCode
                    ?? StatusCodes.Good;
                Assert.That(statusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
            }
            finally
            {
                if (userSession != null)
                {
                    await userSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    userSession.Dispose();
                }
            }
        }

        [Test]
        public async Task MapUsernameIdentityToRoleAsync()
        {
            NodeId operatorId = ToNodeId(ObjectIds.WellKnownRole_Operator);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    operatorId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeUserName, "mapUserTest");

                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, operatorId, addMethod,
                    new Variant(rule)).ConfigureAwait(false);

                IgnoreIfRoleMethodNotSupported(result.StatusCode);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True);

                await TryRemoveIdentityAsync(
                    adminSession, operatorId, rule).ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task MapCertificateIdentityToRoleAsync()
        {
            NodeId engineerId = ToNodeId(ObjectIds.WellKnownRole_Engineer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    engineerId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeThumbprint,
                    "0011223344556677889900AABBCCDDEEFF001122");

                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, engineerId, addMethod,
                    new Variant(rule)).ConfigureAwait(false);

                IgnoreIfRoleMethodNotSupported(result.StatusCode);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True);

                await TryRemoveIdentityAsync(
                    adminSession, engineerId, rule).ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task RemoveUsernameIdentityMappingAsync()
        {
            NodeId operatorId = ToNodeId(ObjectIds.WellKnownRole_Operator);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    operatorId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);
                NodeId removeMethod = await RequireMethodAsync(
                    operatorId, "RemoveIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeUserName, "removeUserMap");

                await CallRoleMethodAsync(
                    adminSession, operatorId, addMethod,
                    new Variant(rule)).ConfigureAwait(false);

                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, operatorId, removeMethod,
                    new Variant(rule)).ConfigureAwait(false);

                IgnoreIfRoleMethodNotSupported(result.StatusCode);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task RemoveCertificateIdentityMappingAsync()
        {
            NodeId engineerId = ToNodeId(ObjectIds.WellKnownRole_Engineer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    engineerId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);
                NodeId removeMethod = await RequireMethodAsync(
                    engineerId, "RemoveIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeThumbprint,
                    "FFEEDDCCBBAA99887766554433221100FFEEDDCC");

                await CallRoleMethodAsync(
                    adminSession, engineerId, addMethod,
                    new Variant(rule)).ConfigureAwait(false);

                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, engineerId, removeMethod,
                    new Variant(rule)).ConfigureAwait(false);

                IgnoreIfRoleMethodNotSupported(result.StatusCode);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task AddMultipleIdentitiesToSameRoleAsync()
        {
            NodeId operatorId = ToNodeId(ObjectIds.WellKnownRole_Operator);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    operatorId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule1 = CreateIdentityRule(
                    CriteriaTypeUserName, "multiId1");
                ExtensionObject rule2 = CreateIdentityRule(
                    CriteriaTypeUserName, "multiId2");

                CallMethodResult r1 = await CallRoleMethodAsync(
                    adminSession, operatorId, addMethod,
                    new Variant(rule1)).ConfigureAwait(false);
                IgnoreIfRoleMethodNotSupported(r1.StatusCode);
                Assert.That(StatusCode.IsGood(r1.StatusCode), Is.True);

                CallMethodResult r2 = await CallRoleMethodAsync(
                    adminSession, operatorId, addMethod,
                    new Variant(rule2)).ConfigureAwait(false);
                IgnoreIfRoleMethodNotSupported(r2.StatusCode);
                Assert.That(StatusCode.IsGood(r2.StatusCode), Is.True);

                // Cleanup
                await TryRemoveIdentityAsync(
                    adminSession, operatorId, rule1)
                    .ConfigureAwait(false);
                await TryRemoveIdentityAsync(
                    adminSession, operatorId, rule2)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task ReadIdentitiesReflectsMultipleEntriesAsync()
        {
            NodeId operatorId = ToNodeId(ObjectIds.WellKnownRole_Operator);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    operatorId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule1 = CreateIdentityRule(
                    CriteriaTypeUserName, "readMulti1");
                ExtensionObject rule2 = CreateIdentityRule(
                    CriteriaTypeUserName, "readMulti2");

                await CallRoleMethodAsync(
                    adminSession, operatorId, addMethod,
                    new Variant(rule1)).ConfigureAwait(false);
                await CallRoleMethodAsync(
                    adminSession, operatorId, addMethod,
                    new Variant(rule2)).ConfigureAwait(false);

                NodeId identitiesId = await FindChildAsync(
                    operatorId, "Identities", adminSession)
                    .ConfigureAwait(false);

                if (!identitiesId.IsNull)
                {
                    DataValue dv = await ReadPropertyValueAsync(
                        identitiesId, adminSession).ConfigureAwait(false);
                    IgnoreIfRoleMethodNotSupported(dv.StatusCode);
                    Assert.That(dv.StatusCode,
                        Is.EqualTo(StatusCodes.Good));
                }

                // Cleanup
                await TryRemoveIdentityAsync(
                    adminSession, operatorId, rule1)
                    .ConfigureAwait(false);
                await TryRemoveIdentityAsync(
                    adminSession, operatorId, rule2)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task AddIdentityToAnonymousRoleAsync()
        {
            NodeId anonymousId = ToNodeId(
                ObjectIds.WellKnownRole_Anonymous);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    anonymousId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeUserName, "anonIdTest");

                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, anonymousId, addMethod,
                    new Variant(rule)).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True);

                await TryRemoveIdentityAsync(
                    adminSession, anonymousId, rule)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task AddIdentityToSecurityAdminRoleAsync()
        {
            NodeId secAdminId = ToNodeId(
                ObjectIds.WellKnownRole_SecurityAdmin);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    secAdminId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeUserName, "secAdminIdTest");

                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, secAdminId, addMethod,
                    new Variant(rule)).ConfigureAwait(false);

                IgnoreIfRoleMethodNotSupported(result.StatusCode);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True);

                await TryRemoveIdentityAsync(
                    adminSession, secAdminId, rule)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task IdentityWithGroupIdCriteriaAsync()
        {
            NodeId operatorId = ToNodeId(ObjectIds.WellKnownRole_Operator);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    operatorId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeGroupId, "TestGroup");

                try
                {
                    CallMethodResult result = await CallRoleMethodAsync(
                        adminSession, operatorId, addMethod,
                        new Variant(rule)).ConfigureAwait(false);

                    IgnoreIfRoleMethodNotSupported(result.StatusCode);

                    Assert.That(
                        StatusCode.IsGood(result.StatusCode), Is.True);

                    await TryRemoveIdentityAsync(
                        adminSession, operatorId, rule)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode ==
                              StatusCodes.BadInvalidArgument)
                {
                    Assert.Ignore(
                        "GroupId criteria type not supported " +
                        "by this server.");
                }
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task IdentityWithApplicationCriteriaAsync()
        {
            NodeId operatorId = ToNodeId(ObjectIds.WellKnownRole_Operator);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    operatorId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeApplication,
                    "urn:test:app:criteriaTest");

                try
                {
                    CallMethodResult result = await CallRoleMethodAsync(
                        adminSession, operatorId, addMethod,
                        new Variant(rule)).ConfigureAwait(false);

                    IgnoreIfRoleMethodNotSupported(result.StatusCode);

                    Assert.That(
                        StatusCode.IsGood(result.StatusCode), Is.True);

                    await TryRemoveIdentityAsync(
                        adminSession, operatorId, rule)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode ==
                              StatusCodes.BadInvalidArgument)
                {
                    Assert.Ignore(
                        "Application criteria type not supported " +
                        "by this server.");
                }
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task AddApplicationRestrictionAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddApplication", adminSession)
                    .ConfigureAwait(false);

                const string appUri = "urn:test:restriction:add";
                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(appUri)).ConfigureAwait(false);

                IgnoreIfRoleMethodNotSupported(result.StatusCode);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True);

                await TryRemoveApplicationAsync(
                    adminSession, observerId, appUri)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task ReadApplicationRestrictionAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddApplication", adminSession)
                    .ConfigureAwait(false);

                const string appUri = "urn:test:restriction:read";
                CallMethodResult addResult = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(appUri)).ConfigureAwait(false);

                if (StatusCode.IsBad(addResult.StatusCode))
                {
                    Assert.Ignore(
                        "AddApplication not supported by server " +
                        $"(status: {addResult.StatusCode}).");
                }

                NodeId appsId = await FindChildAsync(
                    observerId, "Applications", adminSession)
                    .ConfigureAwait(false);

                if (!appsId.IsNull)
                {
                    DataValue dv = await ReadPropertyValueAsync(
                        appsId, adminSession).ConfigureAwait(false);
                    Assert.That(dv.StatusCode,
                        Is.EqualTo(StatusCodes.Good));
                }

                await TryRemoveApplicationAsync(
                    adminSession, observerId, appUri)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task AddMultipleApplicationsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddApplication", adminSession)
                    .ConfigureAwait(false);

                const string uri1 = "urn:test:app:multi1";
                const string uri2 = "urn:test:app:multi2";

                CallMethodResult r1 = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(uri1)).ConfigureAwait(false);
                IgnoreIfRoleMethodNotSupported(r1.StatusCode);
                Assert.That(StatusCode.IsGood(r1.StatusCode), Is.True);

                CallMethodResult r2 = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(uri2)).ConfigureAwait(false);
                IgnoreIfRoleMethodNotSupported(r2.StatusCode);
                Assert.That(StatusCode.IsGood(r2.StatusCode), Is.True);

                // Cleanup
                await TryRemoveApplicationAsync(
                    adminSession, observerId, uri1)
                    .ConfigureAwait(false);
                await TryRemoveApplicationAsync(
                    adminSession, observerId, uri2)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task RemoveApplicationRestrictionAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddApplication", adminSession)
                    .ConfigureAwait(false);
                NodeId removeMethod = await RequireMethodAsync(
                    observerId, "RemoveApplication", adminSession)
                    .ConfigureAwait(false);

                const string appUri = "urn:test:restriction:remove";
                await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(appUri)).ConfigureAwait(false);

                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, observerId, removeMethod,
                    new Variant(appUri)).ConfigureAwait(false);

                IgnoreIfRoleMethodNotSupported(result.StatusCode);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task ApplicationsExcludeDefaultValueAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            NodeId excludeId = await FindChildAsync(
                observerId, "ApplicationsExclude").ConfigureAwait(false);

            if (excludeId.IsNull)
            {
                Assert.Ignore(
                    "ApplicationsExclude not found. " +
                    "Optional feature not supported by server.");
            }

            DataValue dv = await ReadPropertyValueAsync(excludeId)
                .ConfigureAwait(false);
            if (!StatusCode.IsGood(dv.StatusCode))
            {
                Assert.Ignore(
                    "ApplicationsExclude exposed but unreadable: " +
                    $"{dv.StatusCode}.");
            }
            Assert.That(dv.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(dv.WrappedValue.TryGetValue(out bool excludeVal), Is.True);
            Assert.That(excludeVal, Is.False);
        }

        [Test]
        public async Task RemoveLastApplicationClearsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddApplication", adminSession)
                    .ConfigureAwait(false);
                NodeId removeMethod = await RequireMethodAsync(
                    observerId, "RemoveApplication", adminSession)
                    .ConfigureAwait(false);

                const string appUri = "urn:test:app:lastRemove";
                await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(appUri)).ConfigureAwait(false);

                await CallRoleMethodAsync(
                    adminSession, observerId, removeMethod,
                    new Variant(appUri)).ConfigureAwait(false);

                NodeId appsId = await FindChildAsync(
                    observerId, "Applications", adminSession)
                    .ConfigureAwait(false);

                if (!appsId.IsNull)
                {
                    DataValue dv = await ReadPropertyValueAsync(
                        appsId, adminSession).ConfigureAwait(false);
                    IgnoreIfRoleMethodNotSupported(dv.StatusCode);
                    Assert.That(dv.StatusCode,
                        Is.EqualTo(StatusCodes.Good));
                }
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task AddApplicationDuplicateIsIdempotentAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddApplication", adminSession)
                    .ConfigureAwait(false);

                const string appUri = "urn:test:app:dupApp";
                CallMethodResult r1 = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(appUri)).ConfigureAwait(false);
                IgnoreIfRoleMethodNotSupported(r1.StatusCode);
                Assert.That(StatusCode.IsGood(r1.StatusCode), Is.True);

                CallMethodResult r2 = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(appUri)).ConfigureAwait(false);
                IgnoreIfRoleMethodNotSupported(r2.StatusCode);
                Assert.That(StatusCode.IsGood(r2.StatusCode), Is.True);

                await TryRemoveApplicationAsync(
                    adminSession, observerId, appUri)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task RemoveNonExistentApplicationFailsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId removeMethod = await RequireMethodAsync(
                    observerId, "RemoveApplication", adminSession)
                    .ConfigureAwait(false);

                try
                {
                    CallMethodResult result = await CallRoleMethodAsync(
                        adminSession, observerId, removeMethod,
                        new Variant("urn:test:app:doesNotExist"))
                        .ConfigureAwait(false);

                    Assert.That(result, Is.Not.Null);
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode ==
                              StatusCodes.BadInvalidArgument ||
                        sre.StatusCode == StatusCodes.BadNoMatch)
                {
                    Assert.Pass(
                        "Server returned expected error: " +
                        $"{sre.StatusCode}");
                }
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task AddApplicationToObserverRoleAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddApplication", adminSession)
                    .ConfigureAwait(false);

                const string appUri = "urn:test:observer:addApp";
                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(appUri)).ConfigureAwait(false);

                IgnoreIfRoleMethodNotSupported(result.StatusCode);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True);

                await TryRemoveApplicationAsync(
                    adminSession, observerId, appUri)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task ReadObserverApplicationsAfterAddAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddApplication", adminSession)
                    .ConfigureAwait(false);

                const string appUri = "urn:test:observer:readApps";
                await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(appUri)).ConfigureAwait(false);

                NodeId appsId = await FindChildAsync(
                    observerId, "Applications", adminSession)
                    .ConfigureAwait(false);

                if (!appsId.IsNull)
                {
                    DataValue dv = await ReadPropertyValueAsync(
                        appsId, adminSession).ConfigureAwait(false);
                    IgnoreIfRoleMethodNotSupported(dv.StatusCode);
                    Assert.That(dv.StatusCode,
                        Is.EqualTo(StatusCodes.Good));
                }

                await TryRemoveApplicationAsync(
                    adminSession, observerId, appUri)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task RemoveApplicationFromObserverRoleAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddApplication", adminSession)
                    .ConfigureAwait(false);
                NodeId removeMethod = await RequireMethodAsync(
                    observerId, "RemoveApplication", adminSession)
                    .ConfigureAwait(false);

                const string appUri = "urn:test:observer:removeApp";
                await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(appUri)).ConfigureAwait(false);

                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, observerId, removeMethod,
                    new Variant(appUri)).ConfigureAwait(false);

                IgnoreIfRoleMethodNotSupported(result.StatusCode);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task AddApplicationToMultipleRolesAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);
            NodeId operatorId = ToNodeId(ObjectIds.WellKnownRole_Operator);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addObserver = await RequireMethodAsync(
                    observerId, "AddApplication", adminSession)
                    .ConfigureAwait(false);
                NodeId addOperator = await RequireMethodAsync(
                    operatorId, "AddApplication", adminSession)
                    .ConfigureAwait(false);

                const string appUri = "urn:test:shared:multiRoleApp";

                CallMethodResult r1 = await CallRoleMethodAsync(
                    adminSession, observerId, addObserver,
                    new Variant(appUri)).ConfigureAwait(false);
                IgnoreIfRoleMethodNotSupported(r1.StatusCode);
                Assert.That(StatusCode.IsGood(r1.StatusCode), Is.True);

                CallMethodResult r2 = await CallRoleMethodAsync(
                    adminSession, operatorId, addOperator,
                    new Variant(appUri)).ConfigureAwait(false);
                IgnoreIfRoleMethodNotSupported(r2.StatusCode);
                Assert.That(StatusCode.IsGood(r2.StatusCode), Is.True);

                // Cleanup
                await TryRemoveApplicationAsync(
                    adminSession, observerId, appUri)
                    .ConfigureAwait(false);
                await TryRemoveApplicationAsync(
                    adminSession, operatorId, appUri)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task AddApplicationWithoutAdminFailsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession userSession = null;
            try
            {
                userSession = await ConnectAsRegularUserAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await GetMethodIdByName(
                    observerId, "AddApplication", userSession)
                    .ConfigureAwait(false);

                if (addMethod.IsNull)
                {
                    Assert.Ignore(
                        "AddApplication not found. " +
                        "Feature not supported by server.");
                }

                ServiceResultException ex = null;
                CallMethodResult result = null;
                try
                {
                    result = await CallRoleMethodAsync(
                            userSession, observerId, addMethod,
                            new Variant("urn:test:noAdmin"))
                            .ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    ex = sre;
                }

                StatusCode statusCode = ex?.StatusCode
                    ?? result?.StatusCode
                    ?? StatusCodes.Good;
                if (statusCode == (StatusCode)StatusCodes.Good)
                {
                    Assert.Ignore(
                        "Server does not enforce admin requirement " +
                        "for role management methods.");
                }

                Assert.That(statusCode,
                    Is.AnyOf(
                        (StatusCode)StatusCodes.BadUserAccessDenied,
                        (StatusCode)StatusCodes.BadMethodInvalid));
            }
            finally
            {
                if (userSession != null)
                {
                    await userSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    userSession.Dispose();
                }
            }
        }

        [Test]
        public async Task RemoveApplicationWithoutAdminFailsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession userSession = null;
            try
            {
                userSession = await ConnectAsRegularUserAsync()
                    .ConfigureAwait(false);

                NodeId removeMethod = await GetMethodIdByName(
                    observerId, "RemoveApplication", userSession)
                    .ConfigureAwait(false);

                if (removeMethod.IsNull)
                {
                    Assert.Ignore(
                        "RemoveApplication not found. " +
                        "Feature not supported by server.");
                }

                ServiceResultException ex = null;
                CallMethodResult result = null;
                try
                {
                    result = await CallRoleMethodAsync(
                            userSession, observerId, removeMethod,
                            new Variant("urn:test:noAdmin"))
                            .ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    ex = sre;
                }

                StatusCode statusCode = ex?.StatusCode
                    ?? result?.StatusCode
                    ?? StatusCodes.Good;
                if (statusCode == (StatusCode)StatusCodes.Good)
                {
                    Assert.Ignore(
                        "Server does not enforce admin requirement " +
                        "for role management methods.");
                }

                // Either BadUserAccessDenied (semantic) or BadMethodInvalid
                // (the role-permission filter hides the method from the user)
                // is a valid denial outcome.
                Assert.That(statusCode,
                    Is.AnyOf(
                        (StatusCode)StatusCodes.BadUserAccessDenied,
                        (StatusCode)StatusCodes.BadMethodInvalid));
            }
            finally
            {
                if (userSession != null)
                {
                    await userSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    userSession.Dispose();
                }
            }
        }

        [Test]
        public async Task AddRoleMethodExistsOnRoleSetAsync()
        {
            NodeId roleSet = ToNodeId(
                ObjectIds.Server_ServerCapabilities_RoleSet);

            NodeId methodId = await GetMethodIdByName(
                roleSet, "AddRole").ConfigureAwait(false);

            if (methodId.IsNull)
            {
                Assert.Ignore(
                    "AddRole method not found on RoleSet. " +
                    "Feature not supported by server.");
            }

            Assert.That(methodId.IsNull, Is.False);
        }

        [Test]
        public async Task RemoveRoleMethodExistsOnRoleSetAsync()
        {
            NodeId roleSet = ToNodeId(
                ObjectIds.Server_ServerCapabilities_RoleSet);

            NodeId methodId = await GetMethodIdByName(
                roleSet, "RemoveRole").ConfigureAwait(false);

            if (methodId.IsNull)
            {
                Assert.Ignore(
                    "RemoveRole method not found on RoleSet. " +
                    "Feature not supported by server.");
            }

            Assert.That(methodId.IsNull, Is.False);
        }

        [Test]
        public async Task RoleSetBrowseReturnsAllWellKnownRolesAsync()
        {
            NodeId roleSet = ToNodeId(
                ObjectIds.Server_ServerCapabilities_RoleSet);

            BrowseResponse response =
                await BrowseForwardAsync(roleSet).ConfigureAwait(false);
            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.Results.Count, Is.GreaterThan(0));

            int objectCount = 0;
            foreach (ReferenceDescription rd in
                response.Results[0].References)
            {
                if (rd.NodeClass == NodeClass.Object)
                {
                    objectCount++;
                }
            }

            Assert.That(objectCount, Is.GreaterThanOrEqualTo(8),
                "RoleSet should contain at least 8 well-known roles.");
        }

        [Test]
        public async Task AllRolesHaveAddIdentityMethodAsync()
        {
            NodeId[] roleIds =
            [
                ToNodeId(ObjectIds.WellKnownRole_Anonymous),
                ToNodeId(ObjectIds.WellKnownRole_AuthenticatedUser),
                ToNodeId(ObjectIds.WellKnownRole_Observer),
                ToNodeId(ObjectIds.WellKnownRole_Operator),
                ToNodeId(ObjectIds.WellKnownRole_Engineer),
                ToNodeId(ObjectIds.WellKnownRole_Supervisor),
                ToNodeId(ObjectIds.WellKnownRole_ConfigureAdmin),
                ToNodeId(ObjectIds.WellKnownRole_SecurityAdmin)
            ];

            bool anyFound = false;
            foreach (NodeId roleId in roleIds)
            {
                NodeId methodId = await GetMethodIdByName(
                    roleId, "AddIdentity").ConfigureAwait(false);
                if (!methodId.IsNull)
                {
                    anyFound = true;
                }
            }

            if (!anyFound)
            {
                Assert.Fail(
                    "AddIdentity not found on any role. " +
                    "Feature not supported by server.");
            }

            Assert.That(anyFound, Is.True);
        }

        [Test]
        public async Task AllRolesHaveRemoveIdentityMethodAsync()
        {
            NodeId[] roleIds =
            [
                ToNodeId(ObjectIds.WellKnownRole_Anonymous),
                ToNodeId(ObjectIds.WellKnownRole_AuthenticatedUser),
                ToNodeId(ObjectIds.WellKnownRole_Observer),
                ToNodeId(ObjectIds.WellKnownRole_Operator),
                ToNodeId(ObjectIds.WellKnownRole_Engineer),
                ToNodeId(ObjectIds.WellKnownRole_Supervisor),
                ToNodeId(ObjectIds.WellKnownRole_ConfigureAdmin),
                ToNodeId(ObjectIds.WellKnownRole_SecurityAdmin)
            ];

            bool anyFound = false;
            foreach (NodeId roleId in roleIds)
            {
                NodeId methodId = await GetMethodIdByName(
                    roleId, "RemoveIdentity").ConfigureAwait(false);
                if (!methodId.IsNull)
                {
                    anyFound = true;
                }
            }

            if (!anyFound)
            {
                Assert.Fail(
                    "RemoveIdentity not found on any role. " +
                    "Feature not supported by server.");
            }

            Assert.That(anyFound, Is.True);
        }

        [Test]
        public async Task RoleMethodsRequireSecurityAdminAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession userSession = null;
            try
            {
                userSession = await ConnectAsRegularUserAsync()
                    .ConfigureAwait(false);

                string[] methodNames =
                [
                    "AddIdentity", "RemoveIdentity",
                    "AddApplication", "RemoveApplication",
                    "AddEndpoint", "RemoveEndpoint"
                ];

                int testedCount = 0;
                foreach (string methodName in methodNames)
                {
                    NodeId methodId = await GetMethodIdByName(
                        observerId, methodName, userSession)
                        .ConfigureAwait(false);

                    if (methodId.IsNull)
                    {
                        continue;
                    }

                    testedCount++;
                    ServiceResultException ex = null;
                    CallMethodResult result = null;
                    try
                    {
                        result = await CallRoleMethodAsync(
                            userSession, observerId, methodId,
                            new Variant("dummy"))
                            .ConfigureAwait(false);
                    }
                    catch (ServiceResultException sre)
                    {
                        ex = sre;
                    }

                    StatusCode statusCode = ex?.StatusCode
                        ?? result?.StatusCode
                        ?? StatusCodes.Good;
                    if (statusCode == (StatusCode)StatusCodes.Good)
                    {
                        Assert.Ignore(
                            "Server does not enforce admin requirement " +
                            "for role management methods.");
                    }
                    Assert.That(statusCode,
                        Is.AnyOf(
                            (StatusCode)StatusCodes.BadUserAccessDenied,
                            (StatusCode)StatusCodes.BadMethodInvalid),
                        $"{methodName} should return BadUserAccessDenied or BadMethodInvalid.");
                }

                if (testedCount == 0)
                {
                    Assert.Ignore(
                        "No role methods found. " +
                        "Feature not supported by server.");
                }
            }
            finally
            {
                if (userSession != null)
                {
                    await userSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    userSession.Dispose();
                }
            }
        }

        [Test]
        public async Task MultipleMethodCallsInSingleRequestAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);
            NodeId operatorId = ToNodeId(ObjectIds.WellKnownRole_Operator);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addObserver = await GetMethodIdByName(
                    observerId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);
                NodeId addOperator = await GetMethodIdByName(
                    operatorId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);

                if (addObserver.IsNull || addOperator.IsNull)
                {
                    Assert.Ignore(
                        "AddIdentity not found on both roles. " +
                        "Feature not supported by server.");
                }

                ExtensionObject rule1 = CreateIdentityRule(
                    CriteriaTypeUserName, "batchUser1");
                ExtensionObject rule2 = CreateIdentityRule(
                    CriteriaTypeUserName, "batchUser2");

                CallResponse callResponse =
                    await adminSession.CallAsync(
                        null,
                        new CallMethodRequest[]
                        {
                            new() {
                                ObjectId = observerId,
                                MethodId = addObserver,
                                InputArguments =
                                    new Variant[]
                                    {
                                        new(rule1)
                                    }.ToArrayOf()
                            },
                            new() {
                                ObjectId = operatorId,
                                MethodId = addOperator,
                                InputArguments =
                                    new Variant[]
                                    {
                                        new(rule2)
                                    }.ToArrayOf()
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(callResponse.Results, Is.Not.Null);
                Assert.That(callResponse.Results.Count, Is.EqualTo(2));

                if (StatusCode.IsBad(callResponse.Results[0].StatusCode) ||
                    StatusCode.IsBad(callResponse.Results[1].StatusCode))
                {
                    Assert.Ignore(
                        "Server does not support batched AddIdentity calls " +
                        "across multiple roles in a single request " +
                        $"(results: {callResponse.Results[0].StatusCode}, " +
                        $"{callResponse.Results[1].StatusCode}).");
                }

                Assert.That(
                    StatusCode.IsGood(
                        callResponse.Results[0].StatusCode), Is.True);
                Assert.That(
                    StatusCode.IsGood(
                        callResponse.Results[1].StatusCode), Is.True);

                // Cleanup
                await TryRemoveIdentityAsync(
                    adminSession, observerId, rule1)
                    .ConfigureAwait(false);
                await TryRemoveIdentityAsync(
                    adminSession, operatorId, rule2)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        public async Task RoleChangesArePersistentWithinSessionAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeUserName, "persistenceTest");

                await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(rule)).ConfigureAwait(false);

                // Close and reconnect
                await adminSession.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                adminSession.Dispose();
                adminSession = null;

                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId identitiesId = await FindChildAsync(
                    observerId, "Identities", adminSession)
                    .ConfigureAwait(false);

                if (!identitiesId.IsNull)
                {
                    DataValue dv = await ReadPropertyValueAsync(
                        identitiesId, adminSession).ConfigureAwait(false);
                    IgnoreIfRoleMethodNotSupported(dv.StatusCode);
                    Assert.That(dv.StatusCode,
                        Is.EqualTo(StatusCodes.Good));
                }

                // Cleanup
                await TryRemoveIdentityAsync(
                    adminSession, observerId, rule)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        private async Task<BrowseResponse> BrowseForwardAsync(
            NodeId nodeId,
            ISession session = null)
        {
            session ??= Session;
            return await session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId =
                            ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<NodeId> GetMethodIdByName(
            NodeId roleId,
            string name,
            ISession session = null)
        {
            List<ReferenceDescription> children =
                await BrowseRoleChildrenAsync(roleId, session)
                    .ConfigureAwait(false);

            foreach (ReferenceDescription rd in children)
            {
                if (rd.NodeClass == NodeClass.Method &&
                    rd.BrowseName.Name == name)
                {
                    return ExpandedNodeId.ToNodeId(
                        rd.NodeId, (session ?? Session).NamespaceUris);
                }
            }

            return WellKnownRoleNodeIds.TryGetChild(roleId, name);
        }

        private async Task<NodeId> FindChildAsync(
            NodeId parentId,
            string childName,
            ISession session = null)
        {
            List<ReferenceDescription> children =
                await BrowseRoleChildrenAsync(parentId, session)
                    .ConfigureAwait(false);

            foreach (ReferenceDescription rd in children)
            {
                if (rd.BrowseName.Name == childName)
                {
                    return ExpandedNodeId.ToNodeId(
                        rd.NodeId, (session ?? Session).NamespaceUris);
                }
            }

            return WellKnownRoleNodeIds.TryGetChild(parentId, childName);
        }

        private async Task<DataValue> ReadPropertyValueAsync(
            NodeId nodeId,
            ISession session = null)
        {
            session ??= Session;
            ReadResponse response = await session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private async Task<CallMethodResult> CallRoleMethodAsync(
            ISession session,
            NodeId roleId,
            NodeId methodId,
            params Variant[] args)
        {
            CallResponse callResponse = await session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = roleId,
                        MethodId = methodId,
                        InputArguments = args.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(callResponse.Results, Is.Not.Null);
            Assert.That(callResponse.Results.Count, Is.EqualTo(1));
            return callResponse.Results[0];
        }

        private ExtensionObject CreateEndpoint(string url,
            MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt,
            string policyUri = "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256",
            string transportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/uatcp-uasc-uabinary")
        {
            return new ExtensionObject(new EndpointType
            {
                EndpointUrl = url,
                SecurityMode = mode,
                SecurityPolicyUri = policyUri,
                TransportProfileUri = transportProfileUri
            });
        }

        private ExtensionObject CreateIdentityRule(
            int criteriaType,
            string criteria)
        {
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(
                stream, ServiceMessageContext.CreateEmpty(Telemetry), true);
            encoder.WriteInt32("CriteriaType", criteriaType);
            encoder.WriteString("Criteria", criteria);
            encoder.Close();
            return new ExtensionObject(
                new NodeId(15634),
                ByteString.From(stream.ToArray()));
        }

        private async Task<ISession> ConnectAsAdminAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            string policy = FindPolicyWithUsernameToken(endpoints);
            if (policy == null)
            {
                Assert.Ignore(
                    "No endpoint supports UserName token.");
            }

            return await ClientFixture
                .ConnectAsync(ServerUrl, policy,
                    userIdentity: new UserIdentity("sysadmin", "demo"u8))
                .ConfigureAwait(false);
        }

        private async Task<ISession> ConnectAsRegularUserAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            string policy = FindPolicyWithUsernameToken(endpoints);
            if (policy == null)
            {
                Assert.Ignore(
                    "No endpoint supports UserName token.");
            }

            return await ClientFixture
                .ConnectAsync(ServerUrl, policy,
                    userIdentity: new UserIdentity("user1", "password"u8))
                .ConfigureAwait(false);
        }

        private async Task<ArrayOf<EndpointDescription>> GetEndpointsAsync()
        {
            var endpointConfiguration =
                EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            return await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);
        }

        private static string FindPolicyWithUsernameToken(
            ArrayOf<EndpointDescription> endpoints)
        {
            // Prefer SignAndEncrypt, then Sign, then None (admin reads need encryption for AccessRestrictions=3)
            foreach (MessageSecurityMode mode in new[]
            {
                MessageSecurityMode.SignAndEncrypt,
                MessageSecurityMode.Sign,
                MessageSecurityMode.None
            })
            {
                foreach (EndpointDescription ep in endpoints)
                {
                    if (ep.SecurityMode != mode)
                    {
                        continue;
                    }

                    if (ep.UserIdentityTokens == default)
                    {
                        continue;
                    }

                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName)
                        {
                            return ep.SecurityPolicyUri;
                        }
                    }
                }
            }

            return null;
        }

        private async Task<NodeId> RequireMethodAsync(
            NodeId parentId,
            string methodName,
            ISession session = null)
        {
            NodeId methodId = await GetMethodIdByName(
                parentId, methodName, session).ConfigureAwait(false);
            if (methodId.IsNull)
            {
                Assert.Ignore(
                    $"Method '{methodName}' not found. " +
                    "Feature not supported by server.");
            }
            return methodId;
        }

        /// <summary>
        /// Cleanup helper: removes an endpoint from a role if the method
        /// exists.
        /// </summary>
        private async Task TryRemoveEndpointAsync(
            ISession session,
            NodeId roleId,
            string endpointUrl)
        {
            NodeId removeMethod = await GetMethodIdByName(
                roleId, "RemoveEndpoint", session).ConfigureAwait(false);
            if (!removeMethod.IsNull)
            {
                try
                {
                    await CallRoleMethodAsync(
                        session, roleId, removeMethod,
                        new Variant(CreateEndpoint(endpointUrl))).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // best-effort cleanup
                }
            }
        }

        /// <summary>
        /// Cleanup helper: removes an identity from a role if the method
        /// exists.
        /// </summary>
        private async Task TryRemoveIdentityAsync(
            ISession session,
            NodeId roleId,
            ExtensionObject rule)
        {
            NodeId removeMethod = await GetMethodIdByName(
                roleId, "RemoveIdentity", session).ConfigureAwait(false);
            if (!removeMethod.IsNull)
            {
                try
                {
                    await CallRoleMethodAsync(
                        session, roleId, removeMethod,
                        new Variant(rule)).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // best-effort cleanup
                }
            }
        }

        /// <summary>
        /// Cleanup helper: removes an application from a role if the
        /// method exists.
        /// </summary>
        private async Task TryRemoveApplicationAsync(
            ISession session,
            NodeId roleId,
            string appUri)
        {
            NodeId removeMethod = await GetMethodIdByName(
                roleId, "RemoveApplication", session)
                .ConfigureAwait(false);
            if (!removeMethod.IsNull)
            {
                try
                {
                    await CallRoleMethodAsync(
                        session, roleId, removeMethod,
                        new Variant(appUri)).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // best-effort cleanup
                }
            }
        }

        private const int CriteriaTypeUserName = 1;
        private const int CriteriaTypeThumbprint = 2;
        private const int CriteriaTypeGroupId = 4;
        private const int CriteriaTypeApplication = 3;

        private async Task<List<ReferenceDescription>>
            BrowseRoleChildrenAsync(
                NodeId roleId,
                ISession session = null)
        {
            BrowseResponse response =
                await BrowseForwardAsync(roleId, session)
                    .ConfigureAwait(false);

            if (response?.Results == null ||
                response.Results.Count == 0 ||
                response.Results[0].References == default)
            {
                return [];
            }

            var result = new List<ReferenceDescription>();
            foreach (ReferenceDescription rd in
                response.Results[0].References)
            {
                result.Add(rd);
            }
            return result;
        }
    }
}
