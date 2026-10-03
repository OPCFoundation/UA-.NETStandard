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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Machinery.Client;
using Opc.Ua.Machinery.Energy;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Machinery.Server.Jobs;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Machinery.Server.StateMachines;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Closes the remaining coverage gaps in Machinery.Server / Client that
    /// the feature suites leave cold: hosting factory resolution, result
    /// transfer edge paths, energy carriers, equipment identification, and
    /// the thin client DI surface.
    /// </summary>
    [TestFixture]
    [Category("Machinery")]
    public sealed class MachineryCoverageTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new MachineryServerFixture();
            m_fixture.Options.MaxConcurrentResultTransfers = 2;
            m_fixture.Options.ResultTransferTimeout = TimeSpan.FromSeconds(30);
            await m_fixture.StartAsync();
            m_context = m_fixture.CreateBuildContext();
            m_logger = LoggerFactory.Create(_ => { }).CreateLogger("Coverage");
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.DisposeAsync();
            }
        }

        [Test]
        public void AddMachineryClientRegistersAndResolvesTheFactory()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(options =>
            {
                options.ApplicationName = "MachineryClientCoverage";
                options.ApplicationUri =
                    "urn:localhost:OPCFoundation:MachineryClientCoverage";
            });
            builder.AddMachineryClient();

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(
                provider.GetRequiredService<MachineryClientFactory>(),
                Is.Not.Null);
            Assert.That(
                provider.GetRequiredService<Func<CancellationToken, Task<MachineryClient>>>(),
                Is.Not.Null);

            Assert.Throws<ArgumentNullException>(
                () => new MachineryClientFactory(null!, NUnitTelemetryContext.Create()));
            Assert.Throws<ArgumentNullException>(
                () => new MachineryClientFactory(
                    _ => Task.FromResult<ManagedSession>(null!),
                    null!));
        }

        [Test]
        public async Task InMemoryResultStoreCoversCapacityFilteringAndAck()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new InMemoryMachineryResultStore(0));

            var store = new InMemoryMachineryResultStore(2);
            store.Add(Result("A"));
            store.Add(Result("B"));
            store.Add(Result("C"));

            Assert.That((await store.GetLatestResultAsync())!.ResultId, Is.EqualTo("C"));
            Assert.That(await store.GetResultByIdAsync("A"), Is.Null);
            Assert.That((await store.GetResultByIdAsync("B"))!.ResultId, Is.EqualTo("B"));

            ArrayOf<string> ids = await store.GetResultIdsAsync(1);
            Assert.That(ids.Count, Is.EqualTo(1));
            Assert.That(ids[0], Is.EqualTo("C"));

            ArrayOf<int> emptyAck = await store.AcknowledgeResultsAsync(default);
            Assert.That(emptyAck.Count, Is.Zero);

            ArrayOf<int> ack = await store.AcknowledgeResultsAsync(
                (ArrayOf<string>)["B", "missing"]);
            Assert.That(ack[0], Is.Zero);
            Assert.That(ack[1], Is.Not.Zero);
            Assert.That((await store.GetResultByIdAsync("B"))!.IsAcknowledged, Is.True);

            Assert.Throws<ArgumentNullException>(() => store.Add(null!));
        }

        [Test]
        public async Task ResultMethodsFilterAckAndTransferEdgePathsAsync()
        {
            ResultManagementState? management = null;
            IMachineHandle<BaseObjectState> machine = await NewMachine("Cov-Results")
                .WithResultManagement(results =>
                {
                    management = results.State;
                    results.WithInMemoryStore(8).WithFileTransfer();
                })
                .BuildAsync();

            await machine.Results!.PublishAsync(Result("R-1", "first"));
            await machine.Results.PublishAsync(Result("R-2", "second"));
            await machine.Results.PublishAsync(
                new MachineryResult(
                    new ResultDataType
                    {
                        ResultMetaData = new ResultMetaDataType { ResultId = "R-empty" }
                    }));

            var outputs = new List<Variant>();
            var errors = new List<ServiceResult>();

            ServiceResult byId = await management!.GetResultById!.CallAsync(
                m_fixture!.Manager.SystemContext,
                management.NodeId,
                new Variant[] { Variant.From("R-1"), Variant.From(0) }.ToArrayOf(),
                errors,
                outputs);
            Assert.That(ServiceResult.IsGood(byId), Is.True, byId.ToString());

            outputs.Clear();
            errors.Clear();
            ServiceResult filtered = await management.GetResultIdListFiltered!.CallAsync(
                m_fixture.Manager.SystemContext,
                management.NodeId,
                new Variant[]
                {
                    Variant.Null,
                    Variant.From(0),
                    Variant.From(1u),
                    Variant.From(0)
                }.ToArrayOf(),
                errors,
                outputs);
            Assert.That(ServiceResult.IsGood(filtered), Is.True, filtered.ToString());

            outputs.Clear();
            errors.Clear();
            ServiceResult ack = await management.AcknowledgeResults!.CallAsync(
                m_fixture.Manager.SystemContext,
                management.NodeId,
                new Variant[]
                {
                    Variant.From((ArrayOf<string>)["R-2", "nope"])
                }.ToArrayOf(),
                errors,
                outputs);
            Assert.That(ServiceResult.IsGood(ack), Is.True, ack.ToString());

            Assert.That(
                (await CallGenerateAsync(management, "R-empty")).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNothingToDo));
            Assert.That(
                (await CallGenerateAsync(management, "missing")).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNotFound));
            Assert.That(
                (await CallGenerateRawAsync(management, Variant.Null)).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));

            ServiceResult stringId = await CallGenerateRawAsync(management, Variant.From("R-1"));
            Assert.That(ServiceResult.IsGood(stringId), Is.True, stringId.ToString());
        }

        [Test]
        public async Task ResultTransferFileApiCoversOpenReadSeekCloseAndCapAsync()
        {
            ResultManagementState? management = null;
            await NewMachine("Cov-Transfer")
                .WithResultManagement(results =>
                {
                    management = results.State;
                    results.WithInMemoryStore().WithFileTransfer();
                })
                .BuildAsync();

            var store = new InMemoryMachineryResultStore();
            store.Add(Result("T-1", "abcdefghijklmnopqrstuvwxyz"));
            var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
            var options = new MachineryServerOptions
            {
                MaxConcurrentResultTransfers = 1,
                ResultTransferTimeout = TimeSpan.FromSeconds(5)
            };
            await using var transfer = new MachineryResultTransferManager(
                management!.ResultTransfer!,
                m_fixture!.Manager,
                store,
                options,
                m_logger!,
                clock);

            (NodeId fileNodeId, uint handle) = await GenerateAsync(management, "T-1");
            var file = (FileState)m_fixture.Manager.FindPredefinedNode(fileNodeId)!;
            Assert.That(
                file.OpenCount!.Value,
                Is.EqualTo((ushort)1),
                "The handle GenerateFileForRead returns is already open.");

            var openOutputs = new List<Variant>();
            ServiceResult openStatus = file.Open!.Call(
                m_fixture.Manager.SystemContext,
                file.NodeId,
                new Variant[] { Variant.From((byte)1) }.ToArrayOf(),
                new List<ServiceResult>(),
                openOutputs);
            Assert.That(ServiceResult.IsGood(openStatus), Is.True, openStatus.ToString());
            Assert.That(openOutputs[0].TryGetValue(out uint openHandle), Is.True);
            Assert.That(openHandle, Is.EqualTo(handle));

            Assert.That(
                file.Open.Call(
                    m_fixture.Manager.SystemContext,
                    file.NodeId,
                    new Variant[] { Variant.From((byte)2) }.ToArrayOf(),
                    new List<ServiceResult>(),
                    new List<Variant>()).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));

            Assert.That(
                file.Write!.Call(
                    m_fixture.Manager.SystemContext,
                    file.NodeId,
                    new Variant[]
                    {
                        Variant.From(handle),
                        Variant.From(new ByteString(new byte[] { 1 }))
                    }.ToArrayOf(),
                    new List<ServiceResult>(),
                    new List<Variant>()).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNotWritable));

            var positionOutputs = new List<Variant>();
            Assert.That(
                ServiceResult.IsGood(
                    file.GetPosition!.Call(
                        m_fixture.Manager.SystemContext,
                        file.NodeId,
                        new Variant[] { Variant.From(handle) }.ToArrayOf(),
                        new List<ServiceResult>(),
                        positionOutputs)),
                Is.True);

            Assert.That(
                file.SetPosition!.Call(
                    m_fixture.Manager.SystemContext,
                    file.NodeId,
                    new Variant[] { Variant.From(handle), Variant.From(3ul) }.ToArrayOf(),
                    new List<ServiceResult>(),
                    new List<Variant>()).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(
                file.SetPosition.Call(
                    m_fixture.Manager.SystemContext,
                    file.NodeId,
                    new Variant[] { Variant.From(handle), Variant.From(9999ul) }.ToArrayOf(),
                    new List<ServiceResult>(),
                    new List<Variant>()).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));

            Assert.That(
                file.Read!.Call(
                    m_fixture.Manager.SystemContext,
                    file.NodeId,
                    new Variant[] { Variant.From(handle), Variant.From(-1) }.ToArrayOf(),
                    new List<ServiceResult>(),
                    new List<Variant>()).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));

            Assert.That(
                (await file.Close!.CallAsync(
                    m_fixture.Manager.SystemContext,
                    file.NodeId,
                    new Variant[] { Variant.From(handle) }.ToArrayOf(),
                    new List<ServiceResult>(),
                    new List<Variant>())).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(
                m_fixture.Manager.FindPredefinedNode(fileNodeId),
                Is.Null,
                "Closing the handle removes the transient file node.");

            (NodeId abandonedFileId, _) = await GenerateAsync(management, "T-1");
            Assert.That(
                (await CallGenerateAsync(management, "T-1")).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadTooManyOperations));

            clock.Advance(TimeSpan.FromSeconds(6));
            (NodeId reclaimedFileId, uint reclaimedHandle) = await GenerateAsync(management, "T-1");
            Assert.That(reclaimedHandle, Is.GreaterThan(0));
            Assert.That(reclaimedFileId.IsNull, Is.False);
            Assert.That(
                m_fixture.Manager.FindPredefinedNode(abandonedFileId),
                Is.Null,
                "The expired handle's file node is reclaimed.");

            await transfer.DisposeAsync();
            Assert.That(
                m_fixture.Manager.FindPredefinedNode(reclaimedFileId),
                Is.Null,
                "Disposing the manager removes the file nodes it still holds.");
            await transfer.DisposeAsync();
        }

        [Test]
        public async Task EquipmentIdentificationDescriptionAndNotificationsAsync()
        {
            IMachineryNotificationPublisher? publisher = null;
            IMachineHandle<BaseObjectState> machine = await NewMachine("Cov-Equip")
                .WithMachineryEquipment(equipment => equipment.AddEquipment(
                    new QualifiedName("Die"),
                    "urn:acme:equipment:die",
                    die => die
                        .WithDescription(new LocalizedText("Upper die"))
                        .WithIdentification(id =>
                        {
                            id.Manufacturer = new LocalizedText("Acme");
                            id.SerialNumber = "DIE-1";
                            id.ManufacturerUri = "urn:acme";
                            id.AssetId = "A-1";
                            id.DeviceClass = "Die";
                            id.Model = new LocalizedText("Die-X");
                            id.ComponentName = new LocalizedText("Upper");
                            id.Location = "Bay 2";
                        })
                        .WithEquipmentLife(
                            remaining: 10,
                            startValue: 100,
                            limitValue: 0,
                            engineeringUnits: new EUInformation
                            {
                                DisplayName = new LocalizedText("cycles")
                            },
                            warningValues: (ArrayOf<double>)[20.0])))
                .WithNotifications(notifications => notifications.Bind(out publisher))
                .BuildAsync();

            Assert.That(publisher, Is.Not.Null);
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await publisher!.PublishAsync(new BaseEventState(null)));

            var notification = new SystemEventState(null);
            notification.Initialize(
                publisher!.Context,
                publisher.Notifier,
                EventSeverity.Low,
                new LocalizedText("coverage"));
            notification.TypeDefinitionId = Opc.Ua.ObjectTypeIds.SystemEventType;
            notification.EventType!.Value = Opc.Ua.ObjectTypeIds.SystemEventType;
            await publisher.PublishAsync(notification);

            // Pull-style Publish registration (executed at BuildAsync post-setup).
            await NewMachine("Cov-Publish")
                .WithNotifications(notifications => notifications.Publish(
                    (state, context, ct) => EmptyEvents(ct)))
                .BuildAsync();

            Assert.That(machine.NodeId.IsNull, Is.False);
        }

        private static async IAsyncEnumerable<SystemEventState> EmptyEvents(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield break;
        }

        [Test]
        public async Task EnergyCarriersMassFlowAndDuplicateResourceAsync()
        {
#pragma warning disable CA2263 // Non-generic GetValues keeps net48 building.
            foreach (MachineryEnergyCarrier carrier in
                (MachineryEnergyCarrier[])Enum.GetValues(typeof(MachineryEnergyCarrier)))
#pragma warning restore CA2263
            {
                Assert.That(
                    MachineryEnergyBuilder.CarrierBrowseName(carrier),
                    Is.Not.Null.And.Not.Empty);
            }

            Assert.Throws<ServiceResultException>(
                () => MachineryEnergyBuilder.CarrierBrowseName((MachineryEnergyCarrier)999));

            await NewMachine("Cov-Energy")
                .WithEnergy(energy =>
                {
                    energy.AddResource(MachineryEnergyCarrier.NaturalGas, gas =>
                        gas.Main
                            .WithApplicationTag("gas/main")
                            .WithStartTime(DateTime.UtcNow.AddHours(-1))
                            .WithNonElectricalEnergy(1.5, 0.25)
                            .WithMassFlow(12.5f, 0.5f)
                            .WithBaseFlow(100f, 290f));
                    energy.AddResource(MachineryEnergyCarrier.CoolingLubricant, cool =>
                        cool.Main.WithApplicationTag("cool/main"));
                    energy.AddResource(MachineryEnergyCarrier.SteamSaturated);
                    energy.AddResource(MachineryEnergyCarrier.HotWater);
                    energy.AddResource(MachineryEnergyCarrier.DieselOil);
                    Assert.Throws<ServiceResultException>(
                        () => energy.AddResource(MachineryEnergyCarrier.NaturalGas));
                })
                .BuildAsync();
        }

        [Test]
        public void HostingFactoriesResolveFromTheServiceProvider()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "MachineryCoverageHosting";
                options.ApplicationUri =
                    "urn:localhost:OPCFoundation:MachineryCoverageHosting";
            });
            builder.AddMachinery(options => options.Parts = MachineryParts.All);
            builder.ConfigureMachinery(_ => { });
            builder.ConfigureMachinery(_ => default);
            builder.ConfigureMachinery((_, _) => default);

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(
                provider.GetRequiredService<MachineryNodeManagerFactory>(),
                Is.Not.Null);
            Assert.That(
                provider.GetRequiredService<IMachineryModelProvider>(),
                Is.Not.Null);
            Assert.That(builder.Services, Has.Count.GreaterThan(0));

            var results = new ServiceCollection();
            IOpcUaServerBuilder resultsBuilder = results.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "MachineryCoverageResults";
                options.ApplicationUri =
                    "urn:localhost:OPCFoundation:MachineryCoverageResults";
            });
            resultsBuilder.AddMachineryResults();
            using ServiceProvider resultsProvider = results.BuildServiceProvider();
            Assert.That(
                resultsProvider.GetRequiredService<MachineryResultNodeManagerFactory>(),
                Is.Not.Null);
        }

        [Test]
        public void OptionsValidationCoversFailureBranches()
        {
            Assert.Throws<ArgumentException>(() =>
                new MachineryServerOptions { InstanceNamespaceUri = string.Empty }.Validate());
            Assert.Throws<ArgumentException>(() =>
                new MachineryServerOptions { InstanceNamespaceUri = "relative" }.Validate());
            Assert.Throws<ArgumentException>(() =>
                new MachineryServerOptions { MaxConcurrentResultTransfers = 0 }.Validate());
            Assert.Throws<ArgumentException>(() =>
                new MachineryServerOptions
                {
                    ResultTransferTimeout = TimeSpan.FromMilliseconds(-1)
                }.Validate());
            new MachineryServerOptions().Validate();

            Assert.Throws<ServiceResultException>(
                () => MachineryPartsValidation.Validate(MachineryParts.None));
            Assert.Throws<ServiceResultException>(
                () => MachineryPartsValidation.Validate(MachineryParts.Energy));
            Assert.Throws<ServiceResultException>(
                () => MachineryPartsValidation.Validate((MachineryParts)0x4000));
            MachineryPartsValidation.Validate(MachineryParts.Jobs | MachineryParts.Result);
        }

        [Test]
        public async Task AdoptedMachineAsNodeAndRichIdentificationAsync()
        {
            var adopted = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("Adopted"),
                DisplayName = LocalizedText.Null
            };

            IMachineHandle<BaseObjectState> machine = await m_context!
                .AddMachine(adopted, new QualifiedName("Adopted"))
                .WithIdentification(id =>
                {
                    id.Manufacturer = new LocalizedText("Acme");
                    id.SerialNumber = "AD-1";
                    id.ProductInstanceUri = "urn:acme:adopted";
                    id.ManufacturerUri = "urn:acme";
                    id.Model = new LocalizedText("Adopted");
                    id.ProductCode = "PC-1";
                    id.HardwareRevision = "HW-1";
                    id.SoftwareRevision = "SW-1";
                    id.DeviceClass = "Press";
                    id.AssetId = "ASSET-1";
                    id.ComponentName = new LocalizedText("Press");
                    id.Location = "Cell A";
                    id.InitialOperationDate = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc);
                    id.YearOfConstruction = 2023;
                    id.MonthOfConstruction = 6;
                    id.Writable = true;
                })
                .Configure((state, _) => state.Description = new LocalizedText("adopted"))
                .BuildAsync();

            Assert.That(machine.AsNode(), Is.Not.Null);
            Assert.Throws<ArgumentNullException>(
                () => m_context.AddMachine<BaseObjectState>(null!));
            Assert.Throws<ServiceResultException>(
                () => m_context.AddMachine(new BaseObjectState(null)));

            Assert.Throws<ServiceResultException>(() =>
            {
                var bad = new MachineryIdentificationData
                {
                    Manufacturer = LocalizedText.Null,
                    SerialNumber = "x"
                };
                bad.Validate();
            });
            Assert.Throws<ServiceResultException>(() =>
            {
                var bad = new MachineryIdentificationData
                {
                    Manufacturer = new LocalizedText("Acme"),
                    SerialNumber = string.Empty
                };
                bad.Validate();
            });
            Assert.Throws<ServiceResultException>(() =>
            {
                var bad = new MachineryIdentificationData
                {
                    Manufacturer = new LocalizedText("Acme"),
                    SerialNumber = "x",
                    MonthOfConstruction = 13
                };
                bad.Validate();
            });
        }

        [Test]
        public void PredefinedMetaDataTreatsTheMaximumTimeAsMissing()
        {
            // OPC 10000-6 treats both ends of the UtcTime range as "no time
            // supplied"; the check must not depend on the host's time zone.
            foreach (DateTimeUtc unset in new[] { DateTimeUtc.MinValue, DateTimeUtc.MaxValue })
            {
                var result = new MachineryResult(
                    new ResultDataType
                    {
                        ResultMetaData = new ResultMetaDataType
                        {
                            ResultId = "unset-time",
                            ExternalRecipeId = "ER",
                            InternalRecipeId = "IR",
                            JobId = "J",
                            ProductId = "P",
                            StepId = "S",
                            CreationTime = unset
                        }
                    });

                ServiceResultException ex = Assert.Throws<ServiceResultException>(
                    () => PredefinedResultMetaData.Validate(result, onIngestion: true))!;
                Assert.That(ex.Message, Does.Contain("CreationTime"));
            }
        }

        [Test]
        public async Task PredefinedMetaDataAndJobParameterValidationAsync()
        {
            Assert.Throws<ArgumentNullException>(
                () => PredefinedResultMetaData.Validate(null!, onIngestion: true));
            Assert.Throws<ServiceResultException>(
                () => PredefinedResultMetaData.Validate(Result("P-1"), onIngestion: true));
            Assert.Throws<ServiceResultException>(
                () => PredefinedResultMetaData.Validate(Result("P-1"), onIngestion: false));

            ResultManagementState? management = null;
            IMachineHandle<BaseObjectState> machine = await NewMachine("Cov-Meta")
                .WithResultManagement(results =>
                {
                    management = results.State;
                    results.WithInMemoryStore().WithPredefinedResultMetaData();
                })
                .BuildAsync();

            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await machine.Results!.PublishAsync(Result("incomplete")));

            await machine.Results!.PublishAsync(
                new MachineryResult(
                    new ResultDataType
                    {
                        ResultMetaData = new ResultMetaDataType
                        {
                            ResultId = "complete",
                            ExternalRecipeId = "ER",
                            InternalRecipeId = "IR",
                            JobId = "J",
                            ProductId = "P",
                            StepId = "S",
                            CreationTime = DateTime.UtcNow
                        }
                    }));
            Assert.That(management, Is.Not.Null);

            // Job-order-only parameter in a response is refused.
            Assert.Throws<ServiceResultException>(() =>
                MachineryJobParameters.Validate(
                    new[]
                    {
                        new V2.ISA95ParameterDataType
                        {
                            ID = "PlannedProductionTime",
                            Value = Variant.From(1.0)
                        }
                    }.ToArrayOf(),
                    inJobOrder: false,
                    subject: "response"));

            // Wrong built-in type for a known parameter.
            Assert.Throws<ServiceResultException>(() =>
                MachineryJobParameters.Validate(
                    new[]
                    {
                        new V2.ISA95ParameterDataType
                        {
                            ID = "JobName",
                            Value = Variant.From(42)
                        }
                    }.ToArrayOf(),
                    inJobOrder: true,
                    subject: "order"));

            // Unknown / null value travel untouched.
            MachineryJobParameters.Validate(
                new[]
                {
                    new V2.ISA95ParameterDataType
                    {
                        ID = "CustomVendorParam",
                        Value = Variant.From("ok")
                    },
                    new V2.ISA95ParameterDataType
                    {
                        ID = "JobName",
                        Value = Variant.Null
                    }
                }.ToArrayOf(),
                inJobOrder: true,
                subject: "order");
        }

        [Test]
        public async Task EnergyMeasurementApiAndTransferHandleErrorsAsync()
        {
            await NewMachine("Cov-EnergyApi")
                .WithEnergy(energy => energy.AddResource(
                    MachineryEnergyCarrier.Electricity,
                    electricity =>
                    {
                        electricity.Main
                            .WithApplicationTag("elec/main")
                            .WithInterface(
                                NodeId.Create(
                                    Opc.Ua.Machinery.Energy.ObjectTypes.INonElectricalEnergyType,
                                    Opc.Ua.Machinery.Energy.Namespaces.MachineryEnergy,
                                    m_fixture!.Manager.SystemContext.NamespaceUris))
                            .AddMeasurementValue(
                                new QualifiedName("AcActivePowerTotal"),
                                Variant.From(12.5),
                                measurementId: 1)
                            .AddMeasurementValue(
                                new QualifiedName("CustomInt"),
                                Variant.From(7));
                        // Second WithInterface call hits the already-present branch.
                        electricity.Main.WithInterface(
                            NodeId.Create(
                                Opc.Ua.Machinery.Energy.ObjectTypes.INonElectricalEnergyType,
                                Opc.Ua.Machinery.Energy.Namespaces.MachineryEnergy,
                                m_fixture.Manager.SystemContext.NamespaceUris));
                        Assert.Throws<ServiceResultException>(
                            () => electricity.Main.AddMeasurementValue(
                                default,
                                Variant.From(1)));
                        Assert.Throws<ArgumentNullException>(
                            () => electricity.Main.WithInterface(NodeId.Null));
                    }))
                .BuildAsync();

            ResultManagementState? management = null;
            await NewMachine("Cov-HandleErr")
                .WithResultManagement(results =>
                {
                    management = results.State;
                    results.WithInMemoryStore().WithFileTransfer();
                })
                .BuildAsync();

            var store = new InMemoryMachineryResultStore();
            store.Add(Result("H-1", "payload"));
            await using var transfer = new MachineryResultTransferManager(
                management!.ResultTransfer!,
                m_fixture!.Manager,
                store,
                new MachineryServerOptions { MaxConcurrentResultTransfers = 2 },
                m_logger!);

            (NodeId fileNodeId, uint handle) = await GenerateAsync(management, "H-1");
            var file = (FileState)m_fixture.Manager.FindPredefinedNode(fileNodeId)!;

            Assert.That(
                file.Read!.Call(
                    m_fixture.Manager.SystemContext,
                    file.NodeId,
                    new Variant[] { Variant.From(999u), Variant.From(4) }.ToArrayOf(),
                    new List<ServiceResult>(),
                    new List<Variant>()).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
            Assert.That(
                (await file.Close!.CallAsync(
                    m_fixture.Manager.SystemContext,
                    file.NodeId,
                    new Variant[] { Variant.From(handle) }.ToArrayOf(),
                    new List<ServiceResult>(),
                    new List<Variant>())).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.Good));

            await transfer.DisposeAsync();
            Assert.That(
                (await CallGenerateAsync(management, "H-1")).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
        }

        [Test]
        public void DirectBuildContextServiceAccessorsBehave()
        {
            Assert.Throws<InvalidOperationException>(
                () => m_context!.GetRequiredService<object>());
            Assert.That(m_context!.GetService<object>(), Is.Null);
        }

        [Test]
        public async Task ModelProviderUtilitiesFactoriesAndInvalidTransitionAsync()
        {
            Assert.Throws<ArgumentNullException>(
                () => MachineryModelProviderUtilities.Normalize(default, MachineryParts.All));
            Assert.Throws<ServiceResultException>(
                () => MachineryModelProviderUtilities.Normalize(
                    ArrayOf<IMachineryModelProvider>.Empty,
                    MachineryParts.All));
            Assert.Throws<ArgumentException>(
                () => MachineryModelProviderUtilities.Normalize(
                    new IMachineryModelProvider[] { null! }.ToArrayOf(),
                    MachineryParts.All));

            var nullNamespaces = new StubModelProvider(namespaceUris: default);
            Assert.Throws<ServiceResultException>(
                () => MachineryModelProviderUtilities.Normalize(
                    new IMachineryModelProvider[]
                    {
                        new MachineryModelProvider(MachineryParts.BuildingBlocks),
                        nullNamespaces
                    }.ToArrayOf(),
                    MachineryParts.BuildingBlocks));

            var emptyNamespace = new StubModelProvider(
                (ArrayOf<string>)[" "]);
            Assert.Throws<ServiceResultException>(
                () => MachineryModelProviderUtilities.Normalize(
                    new IMachineryModelProvider[]
                    {
                        new MachineryModelProvider(MachineryParts.BuildingBlocks),
                        emptyNamespace
                    }.ToArrayOf(),
                    MachineryParts.BuildingBlocks));

            var incomplete = new StubModelProvider(
                new[] { Opc.Ua.Machinery.Namespaces.Machinery }.ToArrayOf());
            Assert.Throws<ServiceResultException>(
                () => MachineryModelProviderUtilities.Normalize(
                    new IMachineryModelProvider[] { incomplete }.ToArrayOf(),
                    MachineryParts.BuildingBlocks));

            var builtIn = new MachineryModelProvider(MachineryParts.BuildingBlocks);
            var companion = new StubModelProvider(
                builtIn.NamespaceUris,
                order: 10);
            IMachineryModelProvider[] normalized =
                [.. MachineryModelProviderUtilities.Normalize(
                    new IMachineryModelProvider[] { companion, builtIn }.ToArrayOf(),
                    MachineryParts.BuildingBlocks)];
            Assert.That(normalized[0], Is.SameAs(builtIn));

            var options = new MachineryServerOptions
            {
                Parts = MachineryParts.BuildingBlocks
            };
            Assert.That(
                MachineryModelProviderUtilities.GetManagerNamespaceUris(
                    new IMachineryModelProvider[] { builtIn }.ToArrayOf(),
                    options),
                Is.Not.Empty);
            Assert.That(
                MachineryModelProviderUtilities.GetFactoryNamespaceUris(
                    new IMachineryModelProvider[] { builtIn }.ToArrayOf(),
                    options).Count,
                Is.GreaterThan(0));

            Assert.Throws<ServiceResultException>(
                () => MachineryModelProviderUtilities.GetFactoryNamespaceUris(
                    new IMachineryModelProvider[] { builtIn }.ToArrayOf(),
                    new MachineryServerOptions
                    {
                        Parts = MachineryParts.BuildingBlocks,
                        InstanceNamespaceUri = Opc.Ua.Machinery.Namespaces.Machinery
                    }));

            // Factory CreateAsync paths.
            var resultFactory = new MachineryResultNodeManagerFactory(
                new MachineryResultServerOptions(),
                new InMemoryMachineryResultStore());
            IAsyncNodeManager resultManager = await resultFactory.CreateAsync(
                m_fixture!.Server.CurrentInstance,
                m_fixture.Configuration);
            Assert.That(resultManager, Is.InstanceOf<MachineryResultNodeManager>());
            ((IDisposable)resultManager).Dispose();

            var machineryFactory = new MachineryNodeManagerFactory(
                new IMachineryModelProvider[]
                {
                    new MachineryModelProvider(MachineryParts.BuildingBlocks)
                }.ToArrayOf(),
                new MachineryServerOptions { Parts = MachineryParts.BuildingBlocks });
            IAsyncNodeManager machineryManager = await machineryFactory.CreateAsync(
                m_fixture.Server.CurrentInstance,
                m_fixture.Configuration);
            Assert.That(machineryManager, Is.InstanceOf<MachineryNodeManager>());
            ((IDisposable)machineryManager).Dispose();

            _ = await NewMachine("Cov-Transition")
                .WithMonitoring(monitoring => monitoring
                    .WithMachineryItemState(MachineryItemStateValue.NotExecuting)
                    .WithOperationMode(MachineryOperationModeValue.Setup))
                .BuildAsync();

            Assert.Throws<ServiceResultException>(
                () => MachineryStateMachineTables.ToStateId((MachineryItemStateValue)999));

            MachineryBuildCoordinator coordinator =
                MachineryBuildCoordinator.Get(m_fixture.Manager);
            Assert.That(coordinator.GetReservedNodeIdCount(9999), Is.Zero);
            Assert.Throws<ArgumentNullException>(
                () => coordinator.ReserveRootBrowseName(
                    null!,
                    m_fixture.Manager.FindPredefinedNode(
                        NodeId.Create(
                            Opc.Ua.Machinery.Objects.Machines,
                            Opc.Ua.Machinery.Namespaces.Machinery,
                            m_fixture.Manager.SystemContext.NamespaceUris))!,
                    new QualifiedName("x")));
            Assert.Throws<ArgumentNullException>(
                () => coordinator.ReserveRootBrowseName(
                    m_fixture.Manager.SystemContext,
                    null!,
                    new QualifiedName("x")));
            Assert.Throws<ServiceResultException>(
                () => coordinator.ReserveRootBrowseName(
                    m_fixture.Manager.SystemContext,
                    new BaseObjectState(null),
                    new QualifiedName("x")));

            await NewMachine("Dup-Name").BuildAsync();
            Assert.ThrowsAsync<ServiceResultException>(
                async () => await NewMachine("Dup-Name").BuildAsync());

            ArrayOf<string> buildingBlockNamespaces =
                MachineryServer.GetNamespaceUris(MachineryParts.BuildingBlocks);
            var required = new List<string>();
            for (int ii = 0; ii < buildingBlockNamespaces.Count; ii++)
            {
                required.Add(buildingBlockNamespaces[ii]);
            }
            required.Add("urn:custom:also-used-as-instance");
            Assert.Throws<ServiceResultException>(
                () => MachineryModelProviderUtilities.GetFactoryNamespaceUris(
                    new IMachineryModelProvider[]
                    {
                        new StubModelProvider(required.ToArrayOf())
                    }.ToArrayOf(),
                    new MachineryServerOptions
                    {
                        Parts = MachineryParts.BuildingBlocks,
                        InstanceNamespaceUri = "urn:custom:also-used-as-instance"
                    }));

            ResultManagementState? management = null;
            await NewMachine("Cov-Pin")
                .WithResultManagement(results =>
                {
                    management = results.State;
                    results.WithInMemoryStore();
                })
                .BuildAsync();
            var binder = new MachineryResultManagementBinder(
                management!,
                m_fixture.Manager.SystemContext);
            Assert.Throws<ArgumentNullException>(() => binder.BindMethods(null!));
            Assert.That(binder.GetPinnedResults(42).Count, Is.Zero);

            await NewMachine("Cov-DataTypes")
                .WithEnergy(energy => energy.AddResource(
                    MachineryEnergyCarrier.Electricity,
                    electricity =>
                    {
                        electricity.Main
                            .AddMeasurementValue(new QualifiedName("F"), Variant.From(1.5f))
                            .AddMeasurementValue(new QualifiedName("U32"), Variant.From(3u))
                            .AddMeasurementValue(new QualifiedName("I64"), Variant.From(4L))
                            .AddMeasurementValue(new QualifiedName("U64"), Variant.From(5UL))
                            .AddMeasurementValue(
                                new QualifiedName("Other"),
                                Variant.From(true));
                    }))
                .BuildAsync();
        }

        [Test]
        public async Task HostedConfigureMachineryPipelineRunsAsync()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "MachineryPipelineCoverage";
                options.ApplicationUri =
                    "urn:localhost:OPCFoundation:MachineryPipelineCoverage";
            });
            builder.AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks);
            builder.ConfigureMachinery(async (context, ct) =>
            {
                await context
                    .AddMachine(new QualifiedName("Pipeline-Machine"))
                    .WithIdentification(id =>
                    {
                        id.Manufacturer = new LocalizedText("Acme");
                        id.SerialNumber = "PIPE-1";
                        id.ProductInstanceUri = "urn:acme:pipe";
                    })
                    .BuildAsync(ct)
                    .ConfigureAwait(false);
            });
            builder.ConfigureMachinery<EmptyMachineryConfigurator>();

            using ServiceProvider provider = services.BuildServiceProvider();
            IDiPostSetupRunner runner = provider.GetRequiredService<IDiPostSetupRunner>();
            await runner.RunAsync(m_fixture!.Manager, CancellationToken.None);

            var children = new List<BaseInstanceState>();
            NodeState machines = m_fixture.Manager.FindPredefinedNode(
                NodeId.Create(
                    Opc.Ua.Machinery.Objects.Machines,
                    Opc.Ua.Machinery.Namespaces.Machinery,
                    m_fixture.Manager.SystemContext.NamespaceUris))!;
            machines.GetChildren(m_fixture.Manager.SystemContext, children);
            Assert.That(
                children.Exists(child => child.BrowseName.Name == "Pipeline-Machine"),
                Is.True);
        }

#pragma warning disable CA1812 // Instantiated by ConfigureMachinery<T> via DI.
        private sealed class EmptyMachineryConfigurator : IMachineryConfigurator
        {
            public ValueTask ConfigureAsync(
                IMachineryBuildContext context,
                CancellationToken cancellationToken)
            {
                return default;
            }
        }
#pragma warning restore CA1812

        private sealed class StubModelProvider : IMachineryModelProvider
        {
            public StubModelProvider(ArrayOf<string> namespaceUris, int order = 0)
            {
                NamespaceUris = namespaceUris;
                Order = order;
            }

            public int Order { get; }

            public ArrayOf<string> NamespaceUris { get; }

            public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
            {
            }
        }

        private IMachineBuilder<BaseObjectState> NewMachine(string name)
        {
            return m_context!
                .AddMachine(new QualifiedName(name))
                .WithIdentification(id =>
                {
                    id.Manufacturer = new LocalizedText("Acme");
                    id.SerialNumber = $"SN-{name}";
                    id.ProductInstanceUri = $"urn:acme:{name}";
                });
        }

        private static MachineryResult Result(string id, string? payload = null)
        {
            return new MachineryResult(
                new ResultDataType
                {
                    ResultMetaData = new ResultMetaDataType { ResultId = id }
                },
                payload == null
                    ? default
                    : new ByteString(Encoding.UTF8.GetBytes(payload)));
        }

        private ValueTask<ServiceResult> CallGenerateAsync(
            ResultManagementState management,
            string resultId)
        {
            return CallGenerateRawAsync(
                management,
                Variant.FromStructure(
                    new ResultTransferOptionsDataType { ResultId = resultId }));
        }

        private ValueTask<ServiceResult> CallGenerateRawAsync(
            ResultManagementState management,
            Variant options)
        {
            return management.ResultTransfer!.GenerateFileForRead!.CallAsync(
                m_fixture!.Manager.SystemContext,
                management.ResultTransfer.NodeId,
                new[] { options }.ToArrayOf(),
                new List<ServiceResult>(),
                new List<Variant>());
        }

        private async Task<(NodeId FileNodeId, uint Handle)> GenerateAsync(
            ResultManagementState management,
            string resultId)
        {
            var outputs = new List<Variant>();
            ServiceResult status = await management.ResultTransfer!.GenerateFileForRead!.CallAsync(
                m_fixture!.Manager.SystemContext,
                management.ResultTransfer.NodeId,
                new[]
                {
                    Variant.FromStructure(
                        new ResultTransferOptionsDataType { ResultId = resultId })
                }.ToArrayOf(),
                new List<ServiceResult>(),
                outputs);
            Assert.That(ServiceResult.IsGood(status), Is.True, status.ToString());
            Assert.That(outputs[0].TryGetValue(out NodeId fileNodeId), Is.True);
            Assert.That(outputs[1].TryGetValue(out uint handle), Is.True);
            return (fileNodeId, handle);
        }

        private MachineryServerFixture? m_fixture;
        private IMachineryBuildContext? m_context;
        private ILogger? m_logger;

        private sealed class ManualTimeProvider : TimeProvider
        {
            public ManualTimeProvider(DateTimeOffset now) => m_now = now;

            public void Advance(TimeSpan delta) => m_now += delta;

            public override DateTimeOffset GetUtcNow() => m_now;

            private DateTimeOffset m_now;
        }
    }
}
