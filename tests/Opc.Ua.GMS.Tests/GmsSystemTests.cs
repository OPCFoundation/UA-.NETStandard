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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Exercises the public GMS system composition boundary on the real shared namespace owner.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class GmsSystemTests
    {
        /// <summary>
        /// Missing mandatory result storage must fail before the root is published.
        /// </summary>
        [Test]
        public async Task MissingResultStoreRejectsStartupWithoutPublishingRootAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = Context(fixture);
            GmsBuilder builder = Builder(context, "MissingResults", new InMemoryGmsProvider(new GmsSystemSnapshot()));
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await builder.BuildAsync().ConfigureAwait(false)))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(context.MachinesFolder.FindChild(context.Context,
                new QualifiedName("MissingResults", context.InstanceNamespaceIndex)), Is.Null);
        }

        /// <summary>
        /// Defaults publish real mandatory descendants and usable results without selecting optional equipment.
        /// </summary>
        [Test]
        public async Task MandatoryDefaultsUseOneNativeRootAndRealResultBindingAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = Context(fixture);
            var store = new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext());
            await using GmsHandle handle = await Builder(
                context, "MandatoryGms", new InMemoryGmsProvider(new GmsSystemSnapshot()))
                .WithResults(store).BuildAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(handle.Machinery.State, Is.SameAs(handle.State));
                Assert.That(handle.State.Identification, Is.TypeOf<GMSIdentificationState>());
                Assert.That(handle.State.Equipment, Is.TypeOf<GMSEquipmentState>());
                Assert.That(handle.State.Monitoring, Is.TypeOf<GMSMonitoringState>());
                Assert.That(handle.State.ResultManagement, Is.TypeOf<GMSResultManagementState>());
                Assert.That(handle.State.Equipment!.Tools, Is.Null);
                Assert.That(handle.State.Equipment.Accessories, Is.Null);
                Assert.That(handle.State.Monitoring!.LoadingMonitoring, Is.Null);
                Assert.That(handle.State.Monitoring.ToolMonitoring, Is.Null);
                Assert.That(handle.State.Identification!.Workspace, Is.Null);
                Assert.That(handle.State.ResultManagement!.ResultTransfer, Is.Null,
                    "Result file transfer is optional and must be explicitly selected.");
            });
            ISession session = await fixture.ConnectAsync().ConfigureAwait(false);
            var results = new GmsResultClient(session, handle.NodeId, NUnitTelemetryContext.Create());
            Assert.That(await results.QueryAsync(1).ConfigureAwait(false), Is.Empty);
            ReadResponse response = await session.ReadAsync(null, 0, TimestampsToReturn.Both,
                [new ReadValueId
                {
                    NodeId = handle.State.Monitoring!.MachineTool!.OperationMode!.NodeId,
                    AttributeId = Attributes.Value
                }], default).ConfigureAwait(false);
            DataValue operation = response.Results[0];
            Assert.That(operation.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            NodeId nodeId = handle.NodeId;
            await handle.DisposeAsync().ConfigureAwait(false);
            Assert.That(fixture.Manager.FindPredefinedNode(nodeId), Is.Null);
        }

        /// <summary>
        /// Basic and BasicSecure follow the endpoints the running server actually publishes.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public async Task SecureProfilesFollowPublishedEndpointsAsync(bool basic256, bool basic, bool basicSecure)
        {
            var policies = new List<ServerSecurityPolicy>
            {
                new() { SecurityMode = MessageSecurityMode.None, SecurityPolicyUri = SecurityPolicies.None }
            };
            var tokens = new List<UserTokenPolicy> { new(UserTokenType.Anonymous) };
            if (basic256)
            {
                policies.Add(new ServerSecurityPolicy
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                });
            }
            if (basicSecure)
            {
                policies.Add(new ServerSecurityPolicy
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Aes128_Sha256_RsaOaep
                });
                tokens.Add(new UserTokenPolicy(UserTokenType.Certificate));
            }
            await using var fixture = new GmsResultServerFixture
            {
                ServerSecurityPolicies = policies.ToArrayOf(),
                UserTokenPolicies = tokens.ToArrayOf()
            };
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = Context(fixture);
            var store = new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext());
            await using GmsHandle handle = await Builder(
                context, "SecureProfiles", new InMemoryGmsProvider(new GmsSystemSnapshot()))
                .WithResults(store).BuildAsync().ConfigureAwait(false);
            string[] profiles = handle.ServerProfiles.ToArray()!;
            const string prefix = "http://opcfoundation.org/UA-Profile/GMS/Server/";
            Assert.That(profiles.Contains(prefix + "Basic"), Is.EqualTo(basic));
            Assert.That(profiles.Contains(prefix + "BasicSecure"), Is.EqualTo(basicSecure));
        }

        /// <summary>
        /// Creates the same context as the fixture's one Machinery/DI owner.
        /// </summary>
        internal static IMachineryBuildContext Context(GmsResultServerFixture fixture)
        {
            return fixture.Manager.CreateMachineryBuildContext(new MachineryServerOptions
            {
                Parts = MachineryParts.BuildingBlocks | MachineryParts.Result,
                InstanceNamespaceUri = GmsResultServerFixture.InstanceNamespace
            });
        }

        /// <summary>
        /// Supplies an explicit identity and observation provider, without global provider resolution.
        /// </summary>
        internal static GmsBuilder Builder(IMachineryBuildContext context, string name, IGmsProvider provider)
        {
            return context.AddGms(name).WithProvider(provider).WithIdentification(identity =>
            {
                identity.Manufacturer = new LocalizedText("GMS system tests");
                identity.SerialNumber = name;
                identity.ProductInstanceUri = "urn:gms:systems:" + name;
            });
        }
    }
}
