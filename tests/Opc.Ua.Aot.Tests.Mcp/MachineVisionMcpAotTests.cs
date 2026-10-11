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

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.MachineVision;
using Opc.Ua.Vision.Perception;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises the released-model tool data path without reflection-based JSON.
    /// </summary>
    public sealed class MachineVisionMcpAotTests
    {
        [Test]
        public async Task ObservationAndWorkflowBoundsAreNativeAotSafeAsync()
        {
            var options = new MachineVisionObservationOptions
            {
                Window = TimeSpan.FromSeconds(60),
                MaximumNotifications = 100,
                Mode = MachineVisionObservationMode.Conditions,
                RefreshConditions = true
            };
            options.Validate();
            await Assert.That(options.MaximumNotifications).IsEqualTo(100);
            await Assert.That(() => VisionMachineTools.ObserveAsync(null!, "i=1", durationSeconds: 61))
                .Throws<ArgumentOutOfRangeException>();
            await Assert.That(() => VisionMachineTools.RelationshipsAsync(null!, "i=1", startIndex: 10001))
                .Throws<ArgumentOutOfRangeException>();
            await Assert.That(() => VisionMachineTools.DownloadResultAsync(null!, "i=1", "result", maximumBytes: 0))
                .Throws<ArgumentOutOfRangeException>();
            await Assert.That(typeof(MachineVisionAssetClientExtensions).Assembly.GetName().Name)
                .IsEqualTo("Opc.Ua.Vision.Machinery.Client");
            await Assert.That(typeof(VisionPose3DDataType).Namespace)
                .IsEqualTo("Opc.Ua.Vision.Perception");
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
        }

        [Test]
        public async Task ReleasedResultSerializationAndToolBoundsWorkWithoutReflectionAsync()
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                logging => logging.SetMinimumLevel(LogLevel.Warning));
            var context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(MachineVisionModels.ControlNamespaceUri);
            context.Factory.Builder.AddOpcUaVisionMachineVision().Commit();
            using var encoder = new JsonEncoder(context);
            encoder.WriteVariant("Result", Variant.FromStructure(new ResultDataType
            {
                ResultId = new ResultIdDataType { Id = "aot-mcp-result" },
                ResultState = 7,
                ResultContent = [Variant.From(42)],
                EncodingMask = (uint)ResultDataTypeFields.ResultContent
            }));
            using var decoder = new JsonDecoder(encoder.CloseAndReturnText(), context);
            Variant value = decoder.ReadVariant("Result");
            bool decoded = value.TryGetStructure<ResultDataType>(context, out ResultDataType? result);
            await Assert.That(decoded).IsTrue();
            await Assert.That(result!.ResultId.Id).IsEqualTo("aot-mcp-result");
            await Assert.That(result.ResultState).IsEqualTo(7);
            await Assert.That(result.ResultContent.Count).IsEqualTo(1);
            bool hasAnswer = result.ResultContent[0].TryGetValue(out int answer);
            await Assert.That(hasAnswer).IsTrue();
            await Assert.That(answer).IsEqualTo(42);
            await Assert.That(() => VisionMachineTools.FileAsync(null!, "i=1", "Read", length: -1))
                .Throws<ArgumentOutOfRangeException>();
        }
    }
}
