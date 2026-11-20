// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opc.Ua.Di;
using Opc.Ua.IA;
using Opc.Ua.Machinery;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.MachineVision;
using Opc.Ua.Vision.MachineVision.AMCM;
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// NativeAOT roots for the released Vision models, encodings, storage, and hosting.
    /// </summary>
    public sealed class MachineVisionAotTests
    {
        [Test]
        public async Task ReleasedModelsEncodingsStorageAndHostingAreReachableAsync()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                logging => logging.SetMinimumLevel(LogLevel.Warning));
            var message = ServiceMessageContext.Create(telemetry);
            foreach (string uri in new VisionModelProvider(MachineVisionParts.All).NamespaceUris)
            {
                message.NamespaceUris.GetIndexOrAppend(uri);
            }
            foreach (string uri in new VisionMachineryStandaloneModelProvider().NamespaceUris)
            {
                message.NamespaceUris.GetIndexOrAppend(uri);
            }
            message.Factory.Builder.AddOpcUaMachineVision().AddOpcUaMachineVisionAMCM().Commit();
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = message.NamespaceUris,
                EncodeableFactory = message.Factory
            };
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            nodes.AddOpcUaIA(context);
            nodes.AddOpcUaMachinery(context);
            nodes.AddOpcUaMachineVision(context);
            nodes.AddOpcUaMachineVisionAMCM(context);
            VisionSystemState control = context.CreateInstanceOfVisionSystemType();
            VisionSystemAssetState asset = context.CreateInstanceOfVisionSystemAssetType();
            await Assert.That(control.VisionStateMachine).IsNotNull();
            await Assert.That(asset.Identification).IsNotNull();
            await Assert.That(control.TypeDefinitionId.NamespaceIndex == asset.TypeDefinitionId.NamespaceIndex)
                .IsFalse();

            var recipe = new RecipeIdExternalDataType
            {
                Id = "aot-recipe",
                Version = "2",
                Hash = ByteString.From(new byte[] { 1, 2, 3 }),
                HashAlgorithm = "SHA256",
                EncodingMask = (uint)(BinaryIdBaseDataTypeFields.Version |
                    BinaryIdBaseDataTypeFields.Hash |
                    BinaryIdBaseDataTypeFields.HashAlgorithm)
            };
            await RoundTripAsync(message, recipe).ConfigureAwait(false);
            var result = new ResultDataType
            {
                ResultId = new ResultIdDataType { Id = "aot-result" },
                ResultState = 1234,
                ExternalRecipeId = recipe,
                IsPartial = true,
                IsSimulated = true,
                ResultContent = [Variant.From(42), Variant.From("native")],
                EncodingMask = (uint)(ResultDataTypeFields.ExternalRecipeId |
                    ResultDataTypeFields.IsSimulated |
                    ResultDataTypeFields.ResultContent)
            };
            await RoundTripAsync(message, result).ConfigureAwait(false);
            await RoundTripAsync(message, new SEMI_E10SystemStateDataType { Id = 2, Priority = 100 })
                .ConfigureAwait(false);
            var store = new InMemoryMachineVisionStore();
            await store.WriteAsync(new MachineVisionDocument(
                MachineVisionDocumentKind.Result, "aot-result", Variant.FromStructure(result), default))
                .ConfigureAwait(false);
            MachineVisionDocument? saved = await store.ReadAsync(MachineVisionDocumentKind.Result, "aot-result")
                .ConfigureAwait(false);
            await Assert.That(saved).IsNotNull();
            bool decoded = saved!.Metadata.TryGetStructure<ResultDataType>(out ResultDataType? stored);
            await Assert.That(decoded).IsTrue();
            await Assert.That(stored!.ResultState).IsEqualTo(1234);

            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(options => options.ApplicationName = "MachineVisionAotServer")
                .AddVision(options => options.MachineVisionParts = MachineVisionParts.All)
                .AddMachineVision();
            services.AddOpcUa().AddClient(options => options.ApplicationName = "MachineVisionAotClient")
                .AddVisionClient();
            using ServiceProvider provider = services.BuildServiceProvider();
            await Assert.That(provider.GetRequiredService<VisionNodeManagerFactory>()).IsNotNull();
            await Assert.That(provider.GetRequiredService<Func<CancellationToken, Task<VisionClient>>>()).IsNotNull();
        }

        private static async Task RoundTripAsync<T>(ServiceMessageContext context, T value)
            where T : class, IEncodeable, new()
        {
            byte[] bytes = BinaryEncoder.EncodeMessage(value, context);
            T decoded = BinaryDecoder.DecodeMessage<T>(bytes, context);
            await Assert.That(decoded.IsEqual(value)).IsTrue();
        }
    }
}
