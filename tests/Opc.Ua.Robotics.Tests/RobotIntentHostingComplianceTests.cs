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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.Robotics.Server.Hosting;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Verifies Robot Intent hosting behaviour required by clauses 9 and 12.
    /// </summary>
    [TestFixture]
    public class RobotIntentHostingComplianceTests
    {
        [Test]
        public async Task DirectBuildContextWithExecutorExecutesIntent()
        {
            await using ComplianceServerFixture fixture = new ComplianceServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var executor = new RecordingExecutor();
            IRobotIntentBuildContext context = fixture.Manager.CreateRobotIntentBuildContext(executor);

            IIntentControllerBuilder builder = await context.AddIntentControllerAsync(
                "DirectController",
                controller => controller.Accepts<WaitIntentDataType>(),
                CancellationToken.None).ConfigureAwait(false);
            fixture.Manager.StartIntentControllerHosts();

            var sessionId = new NodeId("direct-session", 2);
            Assert.That(builder.Host.RequestControl(context.Context, sessionId, out _), Is.True);
            IntentAdmission admission = builder.Host.SubmitIntent(context.Context, sessionId, new WaitIntentDataType
            {
                IntentId = "direct-intent",
                Duration = 1.0
            });

            await WaitAsync(() => executor.StartedIds.Contains("direct-intent")).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.True);
                Assert.That(executor.StartedIds.ToArray(), Does.Contain("direct-intent"));
            });
        }

        [Test]
        public async Task DirectBuildContextWithoutExecutorFailsAtBuildTime()
        {
            await using ComplianceServerFixture fixture = new ComplianceServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IRobotIntentBuildContext context = fixture.Manager.CreateRobotIntentBuildContext();

            Exception? error = null;
            try
            {
                await context.AddIntentControllerAsync(
                    "MissingExecutorController",
                    controller => controller.Accepts<WaitIntentDataType>()).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                error = ex;
            }

            Assert.That(error, Is.TypeOf<InvalidOperationException>()
                .With.Message.Contains("No Robot Intent executor is registered"));
        }

        [Test]
        public void ConfigureRobotIntentForRejectsUnsupportedManagers()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });

            Assert.That(
                () => builder.ConfigureRobotIntentFor<DiNodeManager>(static (_, _) => default),
                Throws.TypeOf<NotSupportedException>().With.Message.Contains("RobotIntentNodeManager"));
        }

        [Test]
        public async Task MotionProfilePublishesProfileAndRequiredFacetUris()
        {
            var executor = new RecordingExecutor();
            await using ComplianceServerFixture fixture = new ComplianceServerFixture();
            await fixture.StartAsync(new MotionProfileRunner(executor)).ConfigureAwait(false);

            string[] profiles = GetPublishedServerProfiles(fixture);
            string[] expectedRobotEntries =
            [
                RobotIntentConformanceUris.Profiles.Motion,
                RobotIntentConformanceUris.Facets.Base,
                RobotIntentConformanceUris.Facets.MotionJoint,
                RobotIntentConformanceUris.Facets.MotionLinear,
                RobotIntentConformanceUris.Facets.Safety,
                RobotIntentConformanceUris.Facets.Description
            ];

            Assert.Multiple(() =>
            {
                Assert.That(profiles, Is.SupersetOf(expectedRobotEntries));
                Assert.That(profiles, Does.Not.Contain(RobotIntentConformanceUris.Profiles.Handling));
                Assert.That(
                    GetRobotIntentProfileUris(ToStringArray(fixture.Manager.ServerProfiles)),
                    Is.EqualTo(new[] { RobotIntentConformanceUris.Profiles.Motion }));
                Assert.That(ToStringArray(fixture.Manager.ServerProfiles), Is.SupersetOf(expectedRobotEntries));
            });
        }

        [Test]
        public async Task FacetOnlyControllerPublishesFacetUrisWithoutProfile()
        {
            var executor = new RecordingExecutor();
            await using ComplianceServerFixture fixture = new ComplianceServerFixture();
            await fixture.StartAsync(new WaitFacetRunner(executor)).ConfigureAwait(false);

            string[] expectedRobotEntries =
            [
                RobotIntentConformanceUris.Facets.Base,
                RobotIntentConformanceUris.Facets.Wait
            ];

            Assert.Multiple(() =>
            {
                Assert.That(GetRobotIntentProfileUris(ToStringArray(fixture.Manager.ServerProfiles)), Is.Empty);
                Assert.That(ToStringArray(fixture.Manager.ServerProfiles), Is.SupersetOf(expectedRobotEntries));
                Assert.That(GetPublishedServerProfiles(fixture), Is.SupersetOf(expectedRobotEntries));
                Assert.That(
                    GetPublishedServerProfiles(fixture),
                    Does.Not.Contain(RobotIntentConformanceUris.Profiles.Motion));
            });
        }

        [Test]
        public async Task FacetInNoProfileIsPublishedWhenControllerClaimsIt()
        {
            var executor = new RecordingExecutor();
            await using ComplianceServerFixture fixture = new ComplianceServerFixture();
            await fixture.StartAsync(new ForceFacetRunner(executor)).ConfigureAwait(false);

            string[] expectedRobotEntries =
            [
                RobotIntentConformanceUris.Facets.Base,
                RobotIntentConformanceUris.Facets.Force
            ];

            Assert.Multiple(() =>
            {
                Assert.That(GetRobotIntentProfileUris(ToStringArray(fixture.Manager.ServerProfiles)), Is.Empty);
                Assert.That(ToStringArray(fixture.Manager.ServerProfiles), Is.SupersetOf(expectedRobotEntries));
                Assert.That(GetPublishedServerProfiles(fixture), Is.SupersetOf(expectedRobotEntries));
                Assert.That(
                    GetPublishedServerProfiles(fixture),
                    Does.Not.Contain(RobotIntentConformanceUris.Profiles.Motion));
            });
        }

        [Test]
        public async Task ExistingServerProfileArrayEntriesSurviveRobotIntentPublication()
        {
            const string existingProfile = "urn:existing-server-profile";
            var executor = new RecordingExecutor();
            await using ComplianceServerFixture fixture = new ComplianceServerFixture();
            await fixture.StartAsync(new WaitFacetRunner(executor), new[] { existingProfile }).ConfigureAwait(false);

            Assert.That(
                GetPublishedServerProfiles(fixture),
                Is.SupersetOf(new[]
                {
                    existingProfile,
                    RobotIntentConformanceUris.Facets.Base,
                    RobotIntentConformanceUris.Facets.Wait
                }));
        }

        [Test]
        public async Task PublicationSkippedBeforeServerObjectExistsPublishesFacetUrisWhenItAppears()
        {
            var executor = new RecordingExecutor();
            await using ComplianceServerFixture fixture = new ComplianceServerFixture();
            await fixture.StartAsync(new WaitFacetRunner(executor)).ConfigureAwait(false);
            ServerObjectState serverObject = fixture.Server.CurrentInstance.ServerObject;
            serverObject.ServerCapabilities!.ServerProfileArray!.Value = Array.Empty<string>().ToArrayOf();

            SetServerObject(fixture.Server.CurrentInstance, null);
            try
            {
                Assert.That(() => InvokePublishServerProfiles(fixture.Manager), Throws.Nothing);
            }
            finally
            {
                SetServerObject(fixture.Server.CurrentInstance, serverObject);
            }
            InvokePublishServerProfiles(fixture.Manager);

            Assert.That(
                GetPublishedServerProfiles(fixture),
                Is.SupersetOf(new[]
                {
                    RobotIntentConformanceUris.Facets.Base,
                    RobotIntentConformanceUris.Facets.Wait
                }));
        }

        [Test]
        public void Clause124ConstantsExposeProfilesAndFacets()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    RobotIntentConformanceUris.Profiles.Motion,
                    Is.EqualTo("http://opcfoundation.org/UA-Profile/RobotIntent/Server/Motion"));
                Assert.That(
                    RobotIntentConformanceUris.Profiles.Handling,
                    Is.EqualTo("http://opcfoundation.org/UA-Profile/RobotIntent/Server/Handling"));
                Assert.That(
                    RobotIntentConformanceUris.Facets.Base,
                    Is.EqualTo("http://opcfoundation.org/UA-Profile/RobotIntent/Facet/Base"));
                Assert.That(
                    RobotIntentConformanceUris.Facets.ProcessArcWeld,
                    Is.EqualTo("http://opcfoundation.org/UA-Profile/RobotIntent/Facet/Process-ArcWeld"));
                Assert.That(
                    RobotIntentConformanceUris.Facets.MissionBranching,
                    Is.EqualTo("http://opcfoundation.org/UA-Profile/RobotIntent/Facet/Mission-Branching"));
                Assert.That(
                    RobotIntentConformanceUris.Facets.InteropVision,
                    Is.EqualTo("http://opcfoundation.org/UA-Profile/RobotIntent/Facet/Interop-Vision"));
            });
        }

        [Test]
        public void EveryFacetNameConstantMapsToClause124Uri()
        {
            FieldInfo[] fields = typeof(RobotIntentConformanceUris.FacetNames)
                .GetFields(BindingFlags.Public | BindingFlags.Static);
            Assert.That(fields, Is.Not.Empty);
            foreach (FieldInfo field in fields)
            {
                string facetName = (string)field.GetRawConstantValue()!;
#if NETSTANDARD || NETFRAMEWORK
                string expectedUri = RobotIntentConformanceUris.FacetBase + facetName.Substring("RI-".Length);
#else
                string expectedUri = string.Concat(
                    RobotIntentConformanceUris.FacetBase,
                    facetName.AsSpan("RI-".Length));
#endif

                Assert.Multiple(() =>
                {
                    Assert.That(
                        RobotIntentConformanceUris.TryGetFacetUri(facetName, out string facetUri),
                        Is.True,
                        field.Name);
                    Assert.That(facetUri, Is.EqualTo(expectedUri), field.Name);
                });
            }
        }

        private static async Task WaitAsync(Func<bool> condition)
        {
            for (int ii = 0; ii < 500; ii++)
            {
                if (condition())
                {
                    return;
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
            Assert.Fail("timed out waiting for the expected condition");
        }

        private static Pose3DDataType Pose()
        {
            return new Pose3DDataType
            {
                FrameId = "base",
                Position = s_doubleValues1.ToArrayOf(),
                Orientation = s_doubleValues2.ToArrayOf()
            };
        }

        private static ArrayOf<KinematicJointDataType> KinematicChain()
        {
            return new[]
            {
                new KinematicJointDataType
                {
                    AxisId = "Axis0",
                    Kind = AxisKindEnum.Revolute,
                    OriginTransform = Pose(),
                    AxisVector = s_doubleValues3.ToArrayOf()
                },
                new KinematicJointDataType
                {
                    AxisId = "Axis1",
                    Kind = AxisKindEnum.Revolute,
                    OriginTransform = Pose(),
                    AxisVector = s_doubleValues4.ToArrayOf()
                }
            }.ToArrayOf();
        }

        private static string[] GetPublishedServerProfiles(ComplianceServerFixture fixture)
        {
            BaseVariableState profileArray = fixture.Server.CurrentInstance
                .ServerObject
                .ServerCapabilities!
                .ServerProfileArray!;
            Assert.That(profileArray.Value.TryGetValue(out ArrayOf<string> profiles), Is.True);
            return ToStringArray(profiles);
        }

        private static string[] ToStringArray(ArrayOf<string> values)
        {
            return values.ToArray() ?? [];
        }

        private static string[] GetRobotIntentProfileUris(string[] entries)
        {
            return entries
                .Where(static entry => entry.StartsWith(
                    RobotIntentConformanceUris.ProfileBase,
                    StringComparison.Ordinal))
                .ToArray();
        }

        private static void InvokePublishServerProfiles(RobotIntentNodeManager manager)
        {
            MethodInfo? method = typeof(RobotIntentNodeManager).GetMethod(
                "PublishServerProfiles",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method!.Invoke(manager, null);
        }

        private static void SetServerObject(IServerInternal server, ServerObjectState? serverObject)
        {
            FieldInfo? field = server
                .GetType()
                .GetField("<ServerObject>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field!.SetValue(server, serverObject);
        }

        private sealed class WaitFacetRunner : IRobotIntentPostSetupRunner
        {
            public WaitFacetRunner(IIntentExecutor executor)
            {
                m_executor = executor;
            }

            public async ValueTask RunAsync(
                AsyncCustomNodeManager manager,
                RobotIntentRootState root,
                RobotIntentServerOptions options,
                CancellationToken cancellationToken)
            {
                var robotIntentManager = (RobotIntentNodeManager)manager;
                IRobotIntentBuildContext context = robotIntentManager.CreateRobotIntentBuildContext(
                    m_executor,
                    cancellationToken);
                await context.AddIntentControllerAsync(
                    "WaitController",
                    controller => controller.Accepts<WaitIntentDataType>(),
                    cancellationToken).ConfigureAwait(false);
            }

            private readonly IIntentExecutor m_executor;
        }

        private sealed class ForceFacetRunner : IRobotIntentPostSetupRunner
        {
            public ForceFacetRunner(IIntentExecutor executor)
            {
                m_executor = executor;
            }

            public async ValueTask RunAsync(
                AsyncCustomNodeManager manager,
                RobotIntentRootState root,
                RobotIntentServerOptions options,
                CancellationToken cancellationToken)
            {
                var robotIntentManager = (RobotIntentNodeManager)manager;
                IRobotIntentBuildContext context = robotIntentManager.CreateRobotIntentBuildContext(
                    m_executor,
                    cancellationToken);
                await context.AddIntentControllerAsync(
                    "ForceController",
                    controller => controller.Accepts<ForceIntentDataType>(),
                    cancellationToken).ConfigureAwait(false);
            }

            private readonly IIntentExecutor m_executor;
        }

        private sealed class MotionProfileRunner : IRobotIntentPostSetupRunner
        {
            public MotionProfileRunner(IIntentExecutor executor)
            {
                m_executor = executor;
            }

            public async ValueTask RunAsync(
                AsyncCustomNodeManager manager,
                RobotIntentRootState root,
                RobotIntentServerOptions options,
                CancellationToken cancellationToken)
            {
                var robotIntentManager = (RobotIntentNodeManager)manager;
                IRobotIntentBuildContext context = robotIntentManager.CreateRobotIntentBuildContext(
                    m_executor,
                    cancellationToken);
                await context.AddIntentControllerAsync(
                    "MotionController",
                    controller =>
                    {
                        IIntentFrameBuilder baseFrame = controller.AddFrame(
                            "Base",
                            "base",
                            FrameRoleEnum.Base,
                            Pose());
                        IIntentFrameBuilder tcpFrame = controller.AddFrame(
                            "Tcp",
                            "tcp",
                            FrameRoleEnum.Tool,
                            Pose(),
                            frame => frame.WithParent(baseFrame));
                        controller.AddTool("Tool", tcpFrame, true);
                        controller.AddLocation("Home", Pose());
                        controller.AddAxis("Axis0", 0, AxisKindEnum.Revolute);
                        controller.AddAxis("Axis1", 1, AxisKindEnum.Revolute);
                        controller.WithSafetyState(new StaticSafetySource());
                        controller.WithDescription(description => description
                            .WithKinematicChain(KinematicChain())
                            .WithLimits(1.0, 1.0, 1.0, 1.0));
                        controller.Accepts<JointMoveIntentDataType>();
                        controller.Accepts<LinearMoveIntentDataType>();
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            private readonly IIntentExecutor m_executor;
        }

        private sealed class StaticSafetySource : IRobotIntentSafetySource
        {
            public ValueTask<RobotIntentSafetySnapshot> ReadAsync(CancellationToken cancellationToken)
            {
                return new ValueTask<RobotIntentSafetySnapshot>(new RobotIntentSafetySnapshot(
                    SafeMotionFunctionEnum.None,
                    false,
                    false,
                    false,
                    0.0,
                    true,
                    LocalizedText.Null));
            }
        }

        private sealed class RecordingExecutor : IIntentExecutor
        {
            public ConcurrentQueue<string> StartedIds { get; } = new();

            public ValueTask<IntentOutcome> ExecuteAsync(
                IntentExecution execution,
                CancellationToken cancellationToken)
            {
                StartedIds.Enqueue(execution.Intent.IntentId ?? execution.IntentId);
                return new ValueTask<IntentOutcome>(IntentOutcome.Success);
            }

            public bool CanCancel(IntentExecution execution)
            {
                return true;
            }
        }

        private sealed class ComplianceServerFixture : IAsyncDisposable
        {
            public StandardServer Server { get; private set; } = null!;

            public RobotIntentNodeManager Manager { get; private set; } = null!;

            public async Task StartAsync(
                IRobotIntentPostSetupRunner? runner = null,
                string[]? existingServerProfiles = null)
            {
                m_fixture = new ServerFixture<StandardServer>(
                    telemetry => new StandardServer(telemetry))
                {
                    AutoAccept = true,
                    SecurityNone = true
                };
                Server = await m_fixture.StartAsync().ConfigureAwait(false);
                if (existingServerProfiles != null)
                {
                    Server.CurrentInstance.ServerObject.ServerCapabilities!.ServerProfileArray!.Value =
                        existingServerProfiles.ToArrayOf();
                }
                Manager = new RobotIntentNodeManager(
                    Server.CurrentInstance,
                    m_fixture.Config,
                    new IRobotIntentModelProvider[] { new RobotIntentModelProvider() },
                    new RobotIntentServerOptions(),
                    runner);
                await Manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
            }

            public async ValueTask DisposeAsync()
            {
                if (Manager != null)
                {
                    await Manager.DisposeAsync().ConfigureAwait(false);
                }
                if (m_fixture != null)
                {
                    await m_fixture.StopAsync().ConfigureAwait(false);
                }
            }

            private ServerFixture<StandardServer>? m_fixture;
        }

        private static readonly double[] s_doubleValues1 = [0.0, 0.0, 0.0];
        private static readonly double[] s_doubleValues2 = [0.0, 0.0, 0.0, 1.0];
        private static readonly double[] s_doubleValues3 = [0.0, 0.0, 1.0];
        private static readonly double[] s_doubleValues4 = [0.0, 1.0, 0.0];
    }
}
