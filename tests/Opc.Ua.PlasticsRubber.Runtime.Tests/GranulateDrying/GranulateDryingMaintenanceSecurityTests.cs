/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.GranulateDrying.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.PlasticsRubber;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.GranulateDrying.Tests
{
    /// <summary>
    /// Exercises late maintenance conditions through their actual variable and refresh dispatch.
    /// </summary>
    [TestFixture]
    [Category("SecurityRegression")]
    public sealed class GranulateDryingMaintenanceSecurityTests
    {
        /// <summary>
        /// Provider observations remain immutable for denied controllers, nonowners and disabled remote control.
        /// </summary>
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        [TestCase(true, false, true)]
        [TestCase(false, false, false)]
        public async Task SecurityR22LateMaintenanceFieldsRejectUnauthorizedValueWritesAsync(
            bool allowed, bool remote, bool interval)
        {
            await ExerciseAsync(allowed, remote, interval).ConfigureAwait(false);
        }

        /// <summary>
        /// A provider update remains authoritative in the exact retained condition-refresh result.
        /// </summary>
        [Test]
        public async Task SecurityR22MaintenanceProviderAndRetainedRefreshPreserveAuthoritativeFieldsAsync()
        {
            await ExerciseAsync(false, false, true).ConfigureAwait(false);
        }

        /// <summary>
        /// The protection is installed before registration and survives optional-member selection.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SecurityR22MaintenanceProtectionSurvivesLateRegistrationAndOptionalMembersAsync(bool interval)
        {
            await ExerciseAsync(true, true, interval).ConfigureAwait(false);
        }

        /// <summary>
        /// Publishes through a real generated device on a socket-free Machinery owner.
        /// </summary>
        private static async Task ExerciseAsync(bool allowed, bool remote, bool interval)
        {
            await using MachineryNodeManager owner = await CreateOwnerAsync().ConfigureAwait(false);
            GranulateDryingRuntime runtime = owner.CreateGranulateDryingRuntime();
            GranulateDryingDeviceOptions basic = GranulateDryingTestHost.BasicOptions("MaintenanceSecurity");
            var maintenance = new GranulateDryingMaintenanceOptions
            {
                Id = "inspection",
                DisplayName = new LocalizedText("Inspection"),
                OptionalMembers = interval ? ["RemainingInterval"] : default
            };
            GranulateDryingDeviceOptions options = basic with
            {
                Maintenance = [maintenance],
                AnalogMetadata = interval
                    ? (basic.AnalogMetadata.ToArray() ?? []).Concat(
                        (GranulateDryingTestHost.FullOptions("unused").AnalogMetadata.ToArray() ?? [])
                        .Where(value => value.Target == GranulateDryingTarget.Maintenance(1) &&
                            value.Member == "RemainingInterval")).ToArrayOf()
                    : basic.AnalogMetadata
            };
            var authority = new DryingAuthority();
            var initial = new List<GranulateDryingValue>(authority.Current.Values.ToArray() ?? [])
            {
                DryingAuthority.Value(GranulateDryingTarget.Maintenance(1), "Status",
                    Variant.From(MaintenanceStatusEnumeration.NOT_DUE))
            };
            if (interval)
            {
                initial.Add(DryingAuthority.Value(GranulateDryingTarget.Maintenance(1), "RemainingInterval",
                    Variant.From(240.0)));
            }
            authority.Current = authority.Current with
            {
                Values = initial.ToArrayOf(), RemoteControlEnabled = remote
            };
            var authorization = new Mock<IGranulateDryingAuthorization>(MockBehavior.Strict);
            authorization.Setup(value => value.CanControl(It.IsAny<ISystemContext>())).Returns(allowed);
            authorization.Setup(value => value.AuthorizeAsync(
                It.IsAny<ISystemContext>(), It.IsAny<GranulateDryingCommand>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<StatusCode>(allowed ? StatusCodes.Good : StatusCodes.BadUserAccessDenied));
            int registrations = 0;
            runtime.RegisteredForTest = async (device, ct) =>
            {
                registrations++;
                MaintenanceAlarmState alarm = FindAlarm(runtime.Context, device);
                Assert.That(owner.FindPredefinedNode(alarm.NodeId), Is.SameAs(alarm));
                await RejectWritesAsync(runtime.Context, alarm).ConfigureAwait(false);
            };
            GranulateDryingDevice device = await runtime.CreateDeviceAsync(
                options, authority.Provider, authorization.Object).ConfigureAwait(false);
            Assert.That(registrations, Is.EqualTo(1));
            MaintenanceAlarmState condition = FindAlarm(runtime.Context, device);
            Assert.That(condition.RemainingInterval != null, Is.EqualTo(interval));
            await RejectWritesAsync(runtime.Context, condition).ConfigureAwait(false);
            Assert.That(condition.Id!.Value, Is.EqualTo("inspection"));
            Assert.That(condition.Status!.Value, Is.EqualTo(MaintenanceStatusEnumeration.NOT_DUE));
            if (interval)
            {
                Assert.That(condition.RemainingInterval!.Value, Is.EqualTo(240.0));
            }
            var next = new List<GranulateDryingValue>
            {
                DryingAuthority.Value(GranulateDryingTarget.Maintenance(1), "Status",
                    Variant.From(MaintenanceStatusEnumeration.DUE), 33)
            };
            if (interval)
            {
                next.Add(DryingAuthority.Value(GranulateDryingTarget.Maintenance(1), "RemainingInterval",
                    Variant.From(0.0), 33));
            }
            await device.PublishAsync(authority.Next(next.ToArray()) with
            {
                Timestamp = DryingAuthority.Time(33)
            }).ConfigureAwait(false);
            await RejectWritesAsync(runtime.Context, condition).ConfigureAwait(false);
            var retained = new List<IFilterTarget>();
            condition.ConditionRefresh(runtime.Context, retained, false);
            MaintenanceAlarmState refreshed = retained.OfType<MaintenanceAlarmState>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(refreshed, Is.SameAs(condition));
                Assert.That(refreshed.Id!.Value, Is.EqualTo("inspection"));
                Assert.That(refreshed.Status!.Value, Is.EqualTo(MaintenanceStatusEnumeration.DUE));
                Assert.That(refreshed.Status.Timestamp, Is.EqualTo(DryingAuthority.Time(33)));
                Assert.That(refreshed.Retain!.Value, Is.True);
                Assert.That(authority.LastCommand, Is.Null);
                Assert.That(device.Revision, Is.EqualTo(2));
            });
            if (interval)
            {
                Assert.That(refreshed.RemainingInterval!.Value, Is.Zero);
                Assert.That(refreshed.RemainingInterval.Timestamp, Is.EqualTo(DryingAuthority.Time(33)));
            }
            authorization.Verify(value => value.AuthorizeAsync(
                It.IsAny<ISystemContext>(), It.IsAny<GranulateDryingCommand>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Resolves the actual dynamically attached alarm rather than a separate fabricated instance.
        /// </summary>
        private static MaintenanceAlarmState FindAlarm(ISystemContext context, GranulateDryingDevice device)
        {
            var children = new List<BaseInstanceState>();
            device.Maintenance(1).GetChildren(context, children);
            return children.OfType<MaintenanceAlarmState>().Single();
        }

        /// <summary>
        /// Uses correctly typed Good/no-timestamp requests so the Value-write permission is the failing boundary.
        /// </summary>
        private static async Task RejectWritesAsync(ISystemContext context, MaintenanceAlarmState condition)
        {
            BaseVariableState?[] fields = [condition.Id, condition.Status, condition.RemainingInterval];
            Variant[] attacks =
                [Variant.From("attacker-id"), Variant.From(MaintenanceStatusEnumeration.NOT_DUE), Variant.From(999.0)];
            for (int i = 0; i < fields.Length; i++)
            {
                BaseVariableState? field = fields[i];
                if (field == null)
                {
                    continue;
                }
                Variant original = field.Value.Copy();
                DateTimeUtc timestamp = field.Timestamp;
                ServiceResult result = await field.WriteAttributeAsync(
                    context, Attributes.Value, NumericRange.Null,
                    new DataValue(attacks[i]), CancellationToken.None).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable), field.BrowseName.ToString());
                    Assert.That(field.Value, Is.EqualTo(original));
                    Assert.That(field.Timestamp, Is.EqualTo(timestamp));
                    Assert.That(field.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
                    Assert.That(field.UserAccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
                });
            }
        }

        /// <summary>
        /// Loads the real compiled dependency closure with mocked transport-independent server services.
        /// </summary>
        private static async Task<MachineryNodeManager> CreateOwnerAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext message = ServiceMessageContext.Create(telemetry);
            message.NamespaceUris.GetIndexOrAppend("urn:gdd-security:instances");
            var server = new Mock<IServerInternal>();
            var master = new Mock<IMasterNodeManager>();
            master.SetupGet(value => value.AsyncNodeManagers).Returns([]);
            server.SetupGet(value => value.NamespaceUris).Returns(message.NamespaceUris);
            server.SetupGet(value => value.ServerUris).Returns(new StringTable());
            server.SetupGet(value => value.Factory).Returns(message.Factory);
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(message.NamespaceUris));
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.SetupGet(value => value.NodeManager).Returns(master.Object);
            server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            var models = new MachineryModelProvider(MachineryParts.BuildingBlocks);
            var drying = new GranulateDryingModelProvider();
            var owner = new MachineryNodeManager(server.Object, new ApplicationConfiguration
            {
                ApplicationUri = "urn:gdd-security:instances",
                ServerConfiguration = new ServerConfiguration()
            }, [models, drying], new MachineryServerOptions
            {
                Parts = MachineryParts.BuildingBlocks,
                InstanceNamespaceUri = "urn:gdd-security:instances"
            });
            try
            {
                NodeStateCollection nodes = new NodeStateCollection().AddOpcUa(owner.SystemContext);
                models.AddPredefinedNodes(nodes, owner.SystemContext);
                drying.AddPredefinedNodes(nodes, owner.SystemContext);
                foreach (NodeState node in nodes)
                {
                    await owner.AddPredefinedNodeAsync(node).ConfigureAwait(false);
                }
                return owner;
            }
            catch
            {
                await owner.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }
}
