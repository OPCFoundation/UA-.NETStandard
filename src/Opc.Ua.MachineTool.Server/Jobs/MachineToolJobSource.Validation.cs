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
using System.IO;

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class MachineToolJobSource
    {
        /// <summary>
        /// Checks finite resource limits before constructing the protocol engine.
        /// </summary>
        private void ValidateOptions()
        {
            if (m_options.MaximumJobOrders is < 1 or > ushort.MaxValue ||
                m_options.MaximumJobResponses is < 1 or > ushort.MaxValue ||
                m_options.MaximumPendingRevisions is < 2 or > ushort.MaxValue ||
                m_options.MaximumSubscribers is < 1 or > ushort.MaxValue ||
                m_options.MaximumPendingChanges is < 1 or > ushort.MaxValue ||
                m_options.MaximumPayloadBytes is < 128 or > 16777216 ||
                m_options.MaximumTextLength is < 1 or > 1048576 ||
                m_options.MaximumArrayLength is < 1 or > ushort.MaxValue ||
                m_options.Jobs.Count > m_options.MaximumArrayLength)
            {
                throw new ArgumentOutOfRangeException(nameof(m_options), "Job source limits are invalid.");
            }
        }

        /// <summary>
        /// Requires identities and a finite, arithmetically representable mapping instead of guessing program data.
        /// </summary>
        private void ValidateDescriptor(MachineToolJobSourceDescriptor descriptor)
        {
            if (descriptor == null)
            {
                throw Invalid("A job descriptor cannot be null.");
            }
            ValidateText(descriptor.JobOrderId, true);
            ValidateText(descriptor.WorkMasterId, true);
            ValidateText(descriptor.ProgramName, true);
            ValidateText(descriptor.CustomerOrderIdentifier, false);
            ValidateText(descriptor.OrderIdentifier, false);
            ValidateText(descriptor.PartSetKey, false);
            ValidateText(descriptor.PartSetName, false);
            if (descriptor.RunsPlanned == 0 || descriptor.CustomerOrderIdentifier == null ||
                descriptor.OrderIdentifier == null || descriptor.PartSetKey == null ||
                descriptor.PartSetName == null ||
                (descriptor.PartSetKey.Length == 0 &&
                    (descriptor.PartsPlannedPerRun != 0 || descriptor.PartSetName.Length != 0)) ||
                (descriptor.PartSetKey.Length != 0 &&
                    (descriptor.PartsPlannedPerRun == 0 || string.IsNullOrWhiteSpace(descriptor.PartSetName))) ||
                (ulong)descriptor.RunsPlanned * descriptor.PartsPlannedPerRun > uint.MaxValue)
            {
                throw Invalid("A descriptor needs a positive run plan and coherent optional aggregate part plan.");
            }
            if (descriptor.PartSetKey.Length != 0)
            {
                ValidateText(descriptor.PartSetKey, true);
            }
        }

        /// <summary>
        /// Copies a protocol payload through the existing bounded binary codec before entering any source gate.
        /// The fixed-capacity buffer bounds total allocation as well as nested strings, arrays and Variant payloads.
        /// </summary>
        /// <typeparam name="T">The generated payload type, constructed without reflection.</typeparam>
        private T CopyPayload<T>(T value, ArrayOf<LocalizedText> comments = default) where T : IEncodeable, new()
        {
            try
            {
                using var stream = new MemoryStream(new byte[m_options.MaximumPayloadBytes], writable: true);
                using (var encoder = new BinaryEncoder(stream, m_encoding, true))
                {
                    encoder.WriteEncodeable(null, value);
                    encoder.WriteLocalizedTextArray(null, comments);
                }
                stream.SetLength(stream.Position);
                stream.Position = 0;
                using var decoder = new BinaryDecoder(stream, m_encoding, true);
                T copy = decoder.ReadEncodeable<T>(null);
                if (!value.IsEqual(copy))
                {
                    throw Invalid("The generated payload's optional-field mask must include its supplied fields.");
                }
                return copy;
            }
            catch (NotSupportedException exception)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The job payload exceeds its fixed encoded-size bound.", exception);
            }
        }

        /// <summary>
        /// Checks the bounded base-monitoring surface while retaining all raw observation values and metadata.
        /// </summary>
        private void ValidateMonitoring(MachineToolSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            if (snapshot.Operation == null || snapshot.Production == null ||
                snapshot.ActiveProgram != new MachineToolProgram() || snapshot.Production.Jobs.Count != 0)
            {
                throw Invalid("Monitoring must not replace source-owned jobs, active-program identity or state.");
            }
            if (snapshot.Equipment != null || snapshot.Prognoses != null)
            {
                throw Invalid("This bounded reference source supports base monitoring, not equipment or prognoses.");
            }
            ValidateSample(snapshot.Operation.Sample);
            int elements = 0;
            AddElements(ref elements, snapshot.Channels.Count);
            AddElements(ref elements, snapshot.Software.Count);
            AddElements(ref elements, snapshot.Spindles.Count);
            AddElements(ref elements, snapshot.Lasers.Count);
            AddElements(ref elements, snapshot.EdmGenerators.Count);
            AddElements(ref elements, snapshot.Stacklights.Count);
            foreach (MachineToolChannel channel in snapshot.Channels)
            {
                if (channel == null)
                {
                    throw Invalid("A monitoring channel cannot be null.");
                }
                ValidateText(channel.Name, true);
                ValidateSample(channel.Sample);
                AddElements(ref elements, channel.CombinedChannels.Count);
                foreach (string name in channel.CombinedChannels)
                {
                    ValidateText(name, true);
                }
            }
            foreach (MachineToolSoftware software in snapshot.Software)
            {
                if (software == null)
                {
                    throw Invalid("A software observation cannot be null.");
                }
                ValidateText(software.Identifier, true);
                ValidateText(software.Revision, false);
                ValidateText(software.Manufacturer.Text, false);
                ValidateText(software.Manufacturer.Locale, false);
            }
            foreach (MachineToolSpindle spindle in snapshot.Spindles)
            {
                if (spindle == null)
                {
                    throw Invalid("A spindle observation cannot be null.");
                }
                ValidateText(spindle.Name, true);
                ValidateSample(spindle.Sample);
            }
            foreach (MachineToolLaser laser in snapshot.Lasers)
            {
                if (laser == null)
                {
                    throw Invalid("A laser observation cannot be null.");
                }
                ValidateText(laser.Name, true);
                ValidateSample(laser.Sample);
            }
            foreach (MachineToolEdmGenerator generator in snapshot.EdmGenerators)
            {
                if (generator == null)
                {
                    throw Invalid("An EDM observation cannot be null.");
                }
                ValidateText(generator.Name, true);
                ValidateSample(generator.Sample);
            }
            foreach (MachineToolStacklight stacklight in snapshot.Stacklights)
            {
                if (stacklight == null)
                {
                    throw Invalid("A stacklight observation cannot be null.");
                }
                ValidateText(stacklight.Name, true);
                AddElements(ref elements, stacklight.Lights.Count);
                foreach (MachineToolLight light in stacklight.Lights)
                {
                    if (light == null)
                    {
                        throw Invalid("A stacklight segment cannot be null.");
                    }
                    ValidateSample(light.Sample);
                }
            }
            if (snapshot.Production.Statistics is MachineToolProductionStatistics statistics)
            {
                ValidateSample(statistics.Sample);
                if (snapshot.Operation.PartsProducedInLifetime is ulong modern &&
                    modern != statistics.PartsProducedInLifetime)
                {
                    throw Invalid("The supplied legacy and modern lifetime counters must describe the same value.");
                }
            }
        }

        /// <summary>
        /// Bounds the entire inventory, not just each individual collection.
        /// </summary>
        private void AddElements(ref int elements, int count)
        {
            if (count > m_options.MaximumArrayLength - elements)
            {
                throw Invalid("The monitoring inventory exceeds its element bound.");
            }
            elements += count;
        }

        /// <summary>
        /// Rejects missing identities or oversized text before it can enter retained observations.
        /// </summary>
        private void ValidateText(string? text, bool required)
        {
            if ((required && string.IsNullOrWhiteSpace(text)) || text?.Length > m_options.MaximumTextLength)
            {
                throw Invalid("Observation text is missing or exceeds the configured limit.");
            }
        }

        /// <summary>
        /// Never upgrades absent acquisition metadata to a Good observation.
        /// </summary>
        private static void ValidateSample(MachineToolSampleInfo sample)
        {
            if (!StatusCode.IsBad(sample.StatusCode) && sample.SourceTimestamp == default)
            {
                throw Invalid("A usable observation requires its original acquisition timestamp.");
            }
        }
    }
}
