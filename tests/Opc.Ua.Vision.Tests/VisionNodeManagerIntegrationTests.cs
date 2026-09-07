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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Server;
using Opc.Ua.Vision.Server.Builders;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Integration tests over a real <see cref="VisionNodeManager"/>
    /// booted inside a <see cref="Opc.Ua.Server.TestFramework.ServerFixture{T}"/>.
    /// These exercise the address-space bootstrap, the fluent build
    /// context, and the small policy surfaces on the manager itself.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed class VisionNodeManagerIntegrationTests
    {
        [Test]
        public async Task CreateAddressSpaceExposesVisionRootUnderServer()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            Assert.That(fixture.Manager.Root, Is.Not.Null);
            Assert.That(fixture.Manager.Root.BrowseName.Name, Is.EqualTo("Vision"));
            Assert.That(fixture.Manager.Root.Sensors, Is.Not.Null,
                "The mandatory Sensors folder must exist under Vision.");
        }

        [Test]
        public async Task ConformanceUnitsIsEmptyByDefault()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            Assert.That(fixture.Manager.ConformanceUnits.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task ServerProfilesIsEmptyWhenNoSensorsOrPipelinesAreAdded()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            Assert.That(fixture.Manager.ServerProfiles.Count, Is.EqualTo(0),
                "No facets are exposed on an empty Vision address space.");
        }

        [Test]
        public async Task CreateVisionBuildContextExposesVisionRootAndInstanceIndex()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            IVisionBuildContext context = fixture.Manager.CreateVisionBuildContext();

            Assert.Multiple(() =>
            {
                Assert.That(context.Root, Is.SameAs(fixture.Manager.Root));
                Assert.That(context.InstanceNamespaceIndex, Is.GreaterThan(0));
                Assert.That(context.VisionNamespaceIndex, Is.GreaterThan(0));
                Assert.That(context.InstanceNamespaceIndex, Is.Not.EqualTo(context.VisionNamespaceIndex));
                Assert.That(context.Nodes, Is.Not.Null);
            });
        }

        [Test]
        public async Task NewNodeIdReturnsExistingNodeIdWhenNodeAlreadyHasOne()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            var existing = new BaseObjectState(null)
            {
                NodeId = new NodeId("Existing", fixture.Manager.NamespaceIndex),
                SymbolicName = "Existing"
            };

            NodeId result = fixture.Manager.New(fixture.Manager.SystemContext, existing);

            Assert.That(result, Is.EqualTo(existing.NodeId));
        }

        [Test]
        public async Task NewNodeIdSynthesisesGuidWhenNoParentAndNoExistingNodeId()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            var orphan = new BaseObjectState(null)
            {
                NodeId = NodeId.Null,
                SymbolicName = "Orphan"
            };

            NodeId result = fixture.Manager.New(fixture.Manager.SystemContext, orphan);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.IdType, Is.EqualTo(IdType.Guid));
        }

        [Test]
        public async Task NewNodeIdBuildsChildPathWhenParentIsPresent()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("Parent", fixture.Manager.NamespaceIndex),
                SymbolicName = "Parent"
            };
            var child = new BaseObjectState(parent)
            {
                NodeId = NodeId.Null,
                SymbolicName = "Child"
            };

            NodeId result = fixture.Manager.New(fixture.Manager.SystemContext, child);

            Assert.That(result.IdType, Is.EqualTo(IdType.String));
            Assert.That(result.IdentifierAsString, Does.Contain("Parent"));
            Assert.That(result.IdentifierAsString, Does.Contain("Child"));
        }

        [Test]
        public async Task AddingImageSensorPublishesFacetProfilesOnServer()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            context.Nodes.AddImageSensor("Cam1", sensor => sensor
                .WithSensorId("cam-1")
                .WithModality(VisionSensorModalityEnum.Area2D)
                .WithRealityKind(VisionRealityKindEnum.Physical)
                .WithResolution(640u, 480u)
                .WithPixelFormat("Mono8"));

            fixture.Manager.PublishServerProfiles();

            ArrayOf<string> profiles = fixture.Manager.ServerProfiles;
            Assert.That(profiles.Count, Is.GreaterThan(0),
                "Adding a sensor must derive at least one facet on the server profile array.");
        }

        [Test]
        public async Task DisposeAsyncCanBeCalledMultipleTimesWithoutThrowing()
        {
            var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            await fixture.DisposeAsync().ConfigureAwait(false);
            Assert.DoesNotThrowAsync(async () => await fixture.DisposeAsync().ConfigureAwait(false));
        }
    }
}
