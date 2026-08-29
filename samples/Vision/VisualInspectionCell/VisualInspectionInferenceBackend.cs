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
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.AI.Inference;

namespace Vision.VisualInspectionCell
{
    internal sealed class VisualInspectionInferenceBackend : IInferenceBackend
    {
        public VisualInspectionInferenceBackend(
            VisualInspectionAnalysisService analysis,
            VisualInspectionCellOptions options)
        {
            m_analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public InferenceSite Site => m_options.InferenceLocation == VisualInspectionInferenceLocation.EdgeOffServer
            ? InferenceSite.EdgeOffServer
            : InferenceSite.OnServer;

        public ValueTask<IReadOnlyList<BackendModel>> ListModelsAsync(
            string? filter,
            uint maxResults,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<BackendModel> models = string.IsNullOrEmpty(filter) ||
                Model.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                ? [Model]
                : [];
            return ValueTask.FromResult(models);
        }

        public ValueTask<InferenceResult> InvokeAsync(
            InferenceRequest request,
            CancellationToken ct)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            ct.ThrowIfCancellationRequested();
            if (!TryReadFixtureName(request.Payload.Span, out string fixture))
            {
                return ValueTask.FromResult(new InferenceResult
                {
                    Ok = false,
                    ContentType = "application/json",
                    ModelUsed = Model.Name,
                    Finish = InferenceFinish.Error,
                    Message = "The inference payload does not name a known fixture."
                });
            }
            IReadOnlyList<MeasuredCharacteristic> measurements = m_analysis.MeasureByName(fixture);
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                fixture,
                confidence = 0.99,
                measurements = Project(measurements)
            });
            return ValueTask.FromResult(new InferenceResult
            {
                Ok = true,
                Payload = payload,
                ContentType = "application/json",
                ModelUsed = Model.Name,
                UsageUnit = "tokens",
                InputUnits = (ulong)Math.Max(1, request.Payload.Length / 4),
                OutputUnits = (ulong)Math.Max(1, payload.Length / 4),
                TotalUnits = (ulong)Math.Max(2, (request.Payload.Length + payload.Length) / 4)
            });
        }

        public ValueTask<BackendProbe> ProbeAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new BackendProbe
            {
                Reachable = true,
                Detail = Model.Name + " in-process"
            });
        }

        private bool TryReadFixtureName(ReadOnlySpan<byte> payload, out string fixture)
        {
            fixture = string.Empty;
            if (payload.IsEmpty)
            {
                return false;
            }
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(payload.ToArray());
            }
            catch (JsonException)
            {
                return false;
            }
            using (document)
            {
                if (TryReadFixtureProperty(document.RootElement, "fixture", out fixture) ||
                    TryReadFixtureProperty(document.RootElement, "fixtureName", out fixture) ||
                    TryReadFixtureProperty(document.RootElement, "image", out fixture))
                {
                    return true;
                }
                if (TryReadChatFixture(document.RootElement, out fixture))
                {
                    return true;
                }
            }
            return false;
        }

        private bool TryReadChatFixture(JsonElement root, out string fixture)
        {
            fixture = string.Empty;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("messages", out JsonElement messages) ||
                messages.ValueKind != JsonValueKind.Array)
            {
                return false;
            }
            foreach (JsonElement message in messages.EnumerateArray())
            {
                if (message.ValueKind == JsonValueKind.Object &&
                    message.TryGetProperty("content", out JsonElement content) &&
                    content.ValueKind == JsonValueKind.String &&
                    TryExtractFixture(content.GetString(), out fixture))
                {
                    return true;
                }
            }
            return false;
        }

        private bool TryReadFixtureProperty(JsonElement root, string propertyName, out string fixture)
        {
            fixture = string.Empty;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty(propertyName, out JsonElement property) ||
                property.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            return TryExtractFixture(property.GetString(), out fixture);
        }

        private bool TryExtractFixture(string? value, out string fixture)
        {
            if (m_analysis.TryResolveFixtureName(value, out fixture))
            {
                return true;
            }
            if (m_analysis.TryResolveFixtureFromUri(value, out fixture))
            {
                return true;
            }
            return false;
        }

        private static object[] Project(IReadOnlyList<MeasuredCharacteristic> measurements)
        {
            var projected = new object[measurements.Count];
            for (int ii = 0; ii < measurements.Count; ii++)
            {
                MeasuredCharacteristic measurement = measurements[ii];
                projected[ii] = new
                {
                    characteristicId = measurement.CharacteristicId,
                    actual = measurement.Actual.ToString("0.###", CultureInfo.InvariantCulture),
                    uncertainty = measurement.Uncertainty.ToString("0.###", CultureInfo.InvariantCulture),
                    unit = "mm"
                };
            }
            return projected;
        }

        public static BackendModel Model { get; } = new()
        {
            Publisher = "sample",
            Name = "bracket-geometry-analyser",
            Version = "1.0.0",
            TaskKind = "dimensional-inspection",
            Framework = "deterministic-pixel-measurement",
            Capabilities = ["vision-measurement"]
        };

        private readonly VisualInspectionAnalysisService m_analysis;
        private readonly VisualInspectionCellOptions m_options;
    }
}
