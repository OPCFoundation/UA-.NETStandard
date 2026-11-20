// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using Opc.Ua.GMS.Contracts;

namespace Opc.Ua.GMS.Server
{
    /// <summary>
    /// Validates complete provider snapshots before storing, publishing, or returning them.
    /// </summary>
    internal static class GmsResultBudget
    {
        /// <summary>
        /// Counts the encoded standard envelope and sidecars, plus the opaque transferable content.
        /// </summary>
        internal static long Measure(GmsResult result, GmsResultLimits limits, IServiceMessageContext context)
        {
            ArrayOf<GmsCharacteristic> characteristics = result.Characteristics;
            ByteString content = result.FileContent;
            if (characteristics.Count > limits.MaximumCharacteristics ||
                result.UsedTools.Count > limits.MaximumUsedTools ||
                content.Length > limits.MaximumFileBytes)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded, "The GMS result exceeds the configured content bounds.");
            }
            var fields = new List<Variant>
            {
                Variant.From(result.Value),
                Variant.From(result.UsedTools),
                result.ProgramName == null ? Variant.Null : Variant.From(result.ProgramName),
                Variant.From(result.MimeType)
            };
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (GmsCharacteristic characteristic in characteristics)
            {
                if (!names.Add(characteristic.Name))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidArgument, "Intermediate characteristic browse names must be unique.");
                }
                fields.Add(Variant.From(characteristic.Name));
                fields.Add(characteristic.Identifier == null ? Variant.Null : Variant.From(characteristic.Identifier));
                fields.Add(characteristic.Formula == null ? Variant.Null : Variant.From(characteristic.Formula));
                fields.Add(Variant.From(characteristic.CharacteristicsClass ?? 0));
                fields.Add(Variant.From(characteristic.IsValid ?? false));
                fields.Add(Variant.From((int)(characteristic.Evaluation ?? 0)));
                AddAnalog(fields, characteristic.ResultValue);
                AddAnalog(fields, characteristic.Nominal);
                AddTolerance(fields, characteristic.LowerTolerance);
                AddTolerance(fields, characteristic.UpperTolerance);
            }
            return (long)limits.Measure(Variant.From(fields.ToArrayOf()), context) + content.Length;
        }

        /// <summary>
        /// Bounds correction metadata before either startup or a runtime sample changes published state.
        /// </summary>
        internal static void MeasureCorrection(
            GmsCorrection correction,
            GmsResultLimits limits,
            IServiceMessageContext context)
        {
            var fields = new List<Variant>
            {
                Variant.From(correction.Identifier),
                Variant.From(correction.CharacteristicIdentifier),
                Variant.From(correction.Description),
                correction.ProgramName == null ? Variant.Null : Variant.From(correction.ProgramName)
            };
            AddAnalog(fields, correction.RelativeValue);
            AddAnalog(fields, correction.AbsoluteValue);
            AddAnalog(fields, correction.LowerControlLimit);
            AddAnalog(fields, correction.UpperControlLimit);
            limits.Measure(Variant.From(fields.ToArrayOf()), context);
        }

        /// <summary>
        /// Counts an optional sample and the mandatory engineering units of an exposed analog node.
        /// </summary>
        private static void AddAnalog(List<Variant> fields, GmsAnalogValue? value)
        {
            if (value != null)
            {
                fields.Add(Variant.From(value.Value));
                fields.Add(Variant.FromStructure(value.EngineeringUnits));
            }
        }

        /// <summary>
        /// Includes the mandatory tolerance-form discriminator when a limit is supplied.
        /// </summary>
        private static void AddTolerance(List<Variant> fields, GmsTolerance? tolerance)
        {
            if (tolerance != null)
            {
                fields.Add(Variant.From((int)tolerance.Form));
                AddAnalog(fields, tolerance.Value);
            }
        }
    }
}
