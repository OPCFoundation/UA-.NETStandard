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

using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.MachineVision;
using Opc.Ua.Vision.Perception;
using Opc.Ua.Vision.Server;
using Amcm = Opc.Ua.Vision.MachineVision.AMCM;

namespace Opc.Ua.Vision.Tests
{
    public sealed partial class MachineVisionRuntimeTests
    {
        [Test]
        public void StandalonePartTwoRegistrationUsesOneOwnerAndOneModelProvider()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(
                options => options.ApplicationName = "VisionModuleTests").AddVision();
            builder.AddVisionMachinery().AddVisionMachinery();
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<IOptions<VisionServerOptions>>().Value.MachineVisionParts,
                Is.EqualTo(MachineVisionParts.Assets));
            Assert.That(services.Count(s => s.ServiceType == typeof(DiAddressSpaceOwnership)), Is.EqualTo(1));
            Assert.That(provider.GetServices<IVisionModelProvider>().Count(
                p => p is VisionMachineryStandaloneModelProvider), Is.EqualTo(1));
            Assert.That(new VisionMachineryStandaloneModelProvider().NamespaceUris.ToArray(),
                Does.Contain(MachineVisionModels.AssetNamespaceUri));
        }

        [Test]
        public void StandalonePartTwoRequiresRegistrationAndRejectsCompetingOwnership()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(
                options => options.ApplicationName = "VisionModuleTests");
            Assert.Throws<System.InvalidOperationException>(() => builder.AddVisionMachinery());
            builder.AddVision();
            services.AddSingleton(new DiAddressSpaceOwnership("OtherOwner"));
            Assert.Throws<System.InvalidOperationException>(() => builder.AddVisionMachinery());
            Assert.Throws<System.ArgumentNullException>(() =>
                OpcUaServerVisionMachineryExtensions.AddVisionMachinery(null!));
            Assert.Throws<System.ArgumentNullException>(() =>
                OpcUaServerVisionMachineryExtensions.AddVisionToMachinery(null!));
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task AssetImageHandlingAspectsUseTypedInstancesAsync(bool receives, bool transmits)
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Assets).ConfigureAwait(false);
            MachineVisionAssetBuilder builder = fixture.CreateBuildContext().AddMachineVisionAsset("Inspection")
                .WithIdentification(Identification("UNIT-1", "urn:vision:unit-1"))
                .WithComponents();
            MachineVisionNodeBuilder aspect = builder.AddImageHandlingAspect("Link", receives, transmits);
            ExpandedNodeId expected = (receives, transmits) switch
            {
                (true, true) => Amcm.ObjectTypeIds.VisionAspectImageTransceiverType,
                (true, false) => Amcm.ObjectTypeIds.VisionAspectImageReceiverType,
                _ => Amcm.ObjectTypeIds.VisionAspectImageTransmitterType
            };
            Assert.That(((BaseObjectState)aspect.State).TypeDefinitionId,
                Is.EqualTo(ExpandedNodeId.ToNodeId(expected, fixture.Manager.SystemContext.NamespaceUris)));
            Assert.Throws<System.ArgumentException>(() => builder.AddImageHandlingAspect("None", false, false));
            ServiceResultException? duplicate = Assert.Throws<ServiceResultException>(() =>
                builder.AddImageHandlingAspect("Link", receives, transmits));
            Assert.That(duplicate!.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
            MachineVisionAssetHandle asset = await builder.BuildAsync().ConfigureAwait(false);
            Assert.That(asset.State.ImageHandlingAspects, Is.Not.Null);
            Assert.That(asset.State.Components, Is.Not.Null);
            Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(aspect.State.NodeId), Is.SameAs(aspect.State));
        }

        [Test]
        public void PerceptionAndPartTwoHaveDistinctClrAndPackageIdentities()
        {
            Assert.That(typeof(VisionPose3DDataType).Namespace, Is.EqualTo("Opc.Ua.Vision.Perception"));
            Assert.That(typeof(VisionPose3DDataType).Assembly.GetName().Name, Is.EqualTo("Opc.Ua.Vision"));
            Assert.That(typeof(VisionSystemState).Assembly.GetName().Name, Is.EqualTo("Opc.Ua.Vision"));
            Assert.That(typeof(Amcm.VisionSystemAssetState).Namespace, Is.EqualTo("Opc.Ua.Vision.MachineVision.AMCM"));
            Assert.That(typeof(Amcm.VisionSystemAssetState).Assembly.GetName().Name,
                Is.EqualTo("Opc.Ua.MachineVision"));
            Assert.That(typeof(MachineVisionAssetBuilder).Assembly.GetName().Name,
                Is.EqualTo("Opc.Ua.MachineVision.Server"));
            Assert.That(typeof(MachineVisionAssetClientExtensions).Assembly.GetName().Name,
                Is.EqualTo("Opc.Ua.MachineVision.Client"));
            Assert.That(typeof(VisionPose3DDataType).Assembly.GetReferencedAssemblies().Select(a => a.Name),
                Has.None.EqualTo("Opc.Ua.MachineVision"));
            Assert.That(typeof(VisionClient).Assembly.GetReferencedAssemblies().Select(a => a.Name),
                Has.None.EqualTo("Opc.Ua.MachineVision.Client"));
            Assert.That(typeof(MachineVisionSystemHandle).Assembly.GetReferencedAssemblies().Select(a => a.Name),
                Has.None.EqualTo("Opc.Ua.MachineVision.Server"));
            Assert.That(Perception.Namespaces.Vision, Is.EqualTo("http://opcfoundation.org/UA/Vision/"));
        }

        [Test]
        public async Task MultipleSystemsShareOneOwnerButKeepStateAndDocumentsIndependentAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.All).ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();
            MachineVisionSystemHandle first = await context.AddMachineVisionSystemAsync("LineOne", Provider().Object)
                .ConfigureAwait(false);
            MachineVisionSystemHandle second = await context.AddMachineVisionSystemAsync("LineTwo", Provider().Object)
                .ConfigureAwait(false);
            MachineVisionAssetHandle firstAsset = await context.AddMachineVisionAsset("LineOneAsset")
                .WithIdentification(Identification("LINE-001", "urn:vision:line-one"))
                .RepresentsSameSystem(first.State).BuildAsync().ConfigureAwait(false);
            MachineVisionAssetHandle secondAsset = await context.AddMachineVisionAsset("LineTwoAsset")
                .WithIdentification(Identification("LINE-002", "urn:vision:line-two"))
                .RepresentsSameSystem(second.State).BuildAsync().ConfigureAwait(false);

            await CallAsync(fixture, first.ControlState, first.ControlState.SelectModeAutomatic!, [])
                .ConfigureAwait(false);
            Assert.That(first.ControlState.CurrentState!.Value.Text, Is.EqualTo("Operational"));
            Assert.That(second.ControlState.CurrentState!.Value.Text, Is.EqualTo("Preoperational"));
            Assert.That(first.State.NodeId, Is.Not.EqualTo(second.State.NodeId));
            Assert.That(firstAsset.State.NodeId, Is.Not.EqualTo(secondAsset.State.NodeId));
            Assert.That(firstAsset.State.ReferenceExists(ReferenceTypeIds.RepresentsSameEntityAs,
                false, first.State.NodeId), Is.True);
            Assert.That(secondAsset.State.ReferenceExists(ReferenceTypeIds.RepresentsSameEntityAs,
                false, second.State.NodeId), Is.True);

            await first.PublishResultAsync(new ResultDataType
            {
                ResultId = new ResultIdDataType { Id = "same-local-id" },
                ResultState = 1
            }).ConfigureAwait(false);
            await second.PublishResultAsync(new ResultDataType
            {
                ResultId = new ResultIdDataType { Id = "same-local-id" },
                ResultState = 2
            }).ConfigureAwait(false);
            MachineVisionDocument? firstResult = await first.Store.ReadAsync(
                MachineVisionDocumentKind.Result, "same-local-id").ConfigureAwait(false);
            MachineVisionDocument? secondResult = await second.Store.ReadAsync(
                MachineVisionDocumentKind.Result, "same-local-id").ConfigureAwait(false);
            Assert.That(firstResult!.Metadata.TryGetStructure<ResultDataType>(out ResultDataType? firstData), Is.True);
            Assert.That(secondResult!.Metadata.TryGetStructure<ResultDataType>(out ResultDataType? secondData),
                Is.True);
            Assert.That(firstData!.ResultState, Is.EqualTo(1));
            Assert.That(secondData!.ResultState, Is.EqualTo(2));
            Assert.That(first.Store, Is.Not.SameAs(second.Store));
        }
    }
}
