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
using Opc.Ua.Machinery.Result;

namespace Opc.Ua.GMS.Contracts
{
    /// <summary>
    /// An immutable Double sample with its engineering units and unmodified acquisition quality.
    /// </summary>
    public sealed class GmsAnalogValue
    {
        /// <summary>
        /// Captures a Double sample; a bad sample may carry a null value rather than a fabricated measurement.
        /// </summary>
        public GmsAnalogValue(in DataValue value, EUInformation engineeringUnits)
        {
            if (engineeringUnits == null)
            {
                throw new ArgumentNullException(nameof(engineeringUnits));
            }
            if (!value.WrappedValue.TryGetValue(out double _) &&
                !(StatusCode.IsBad(value.StatusCode) && value.WrappedValue.IsNull))
            {
                throw new ArgumentException("A Double, or a null value with bad quality, is required.", nameof(value));
            }
            Value = value.Copy();
            m_units = (EUInformation)engineeringUnits.Clone();
        }

        /// <summary>
        /// Gets the scalar sample, retaining its status and timestamps.
        /// </summary>
        public DataValue Value { get; }

        /// <summary>
        /// Gets a detached copy of the sample's engineering-unit metadata.
        /// </summary>
        public EUInformation EngineeringUnits => (EUInformation)m_units.Clone();

        private readonly EUInformation m_units;
    }

    /// <summary>
    /// An absolute tolerance boundary and its normative limit form (OPC 40210 §8.17 and §11.5).
    /// </summary>
    public sealed class GmsTolerance
    {
        /// <summary>
        /// Captures a tolerance boundary without inferring evaluation from it.
        /// </summary>
        public GmsTolerance(GmsAnalogValue value, ToleranceLimitEnum form)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
            if (form is < ToleranceLimitEnum.NoLimit or > ToleranceLimitEnum.NaturalLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(form));
            }
            Form = form;
        }

        /// <summary>
        /// Gets the absolute boundary and its units.
        /// </summary>
        public GmsAnalogValue Value { get; }

        /// <summary>
        /// Gets whether this boundary is absent, predefined, or a natural technological/physical limit.
        /// </summary>
        public ToleranceLimitEnum Form { get; }
    }

    /// <summary>
    /// One characteristic reported in an intermediate result; optional fields remain explicitly absent.
    /// </summary>
    public sealed record GmsCharacteristic
    {
        /// <summary>
        /// Creates a characteristic with a stable instance browse name.
        /// </summary>
        public GmsCharacteristic(string name)
        {
            Name = !string.IsNullOrWhiteSpace(name)
                ? name
                : throw new ArgumentException("A characteristic browse name is required.", nameof(name));
        }

        /// <summary>
        /// Gets the placeholder instance's browse-name text.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the optional unique measured-characteristic identifier.
        /// </summary>
        public string? Identifier { get; init; }

        /// <summary>
        /// Gets the optional actual measurement.
        /// </summary>
        public GmsAnalogValue? ResultValue { get; init; }

        /// <summary>
        /// Gets the optional absolute lower tolerance limit.
        /// </summary>
        public GmsTolerance? LowerTolerance { get; init; }

        /// <summary>
        /// Gets the optional absolute upper tolerance limit.
        /// </summary>
        public GmsTolerance? UpperTolerance { get; init; }

        /// <summary>
        /// Gets the optional nominal geometric specification value.
        /// </summary>
        public GmsAnalogValue? Nominal { get; init; }

        /// <summary>
        /// Gets the optional importance class; higher values mean more important.
        /// </summary>
        public byte? CharacteristicsClass { get; init; }

        /// <summary>
        /// Gets the supplied tolerance evaluation, independently of the data-acquisition quality.
        /// </summary>
        public ResultEvaluationEnum? Evaluation { get; init; }

        /// <summary>
        /// Gets whether the measurement run is geometrically valid, independently of its UA status.
        /// </summary>
        public bool? IsValid { get; init; }

        /// <summary>
        /// Gets the optional human-readable measurement formula.
        /// </summary>
        public string? Formula { get; init; }
    }

    /// <summary>
    /// A calculated deviation for an upstream system, not an instruction to apply a machine correction.
    /// </summary>
    public sealed record GmsCorrection
    {
        /// <summary>
        /// Creates an identified correction with at least one of the two released correction-value forms.
        /// </summary>
        public GmsCorrection(
            string identifier,
            string characteristicIdentifier,
            GmsAnalogValue? relativeValue = null,
            GmsAnalogValue? absoluteValue = null)
        {
            Identifier = !string.IsNullOrWhiteSpace(identifier)
                ? identifier
                : throw new ArgumentException("A correction identifier is required.", nameof(identifier));
            CharacteristicIdentifier = !string.IsNullOrWhiteSpace(characteristicIdentifier)
                ? characteristicIdentifier
                : throw new ArgumentException("A characteristic reference is required.", nameof(characteristicIdentifier));
            if (relativeValue == null && absoluteValue == null)
            {
                throw new ArgumentException("At least one relative or absolute correction value is required.");
            }
            RelativeValue = relativeValue;
            AbsoluteValue = absoluteValue;
        }

        /// <summary>
        /// Gets the stable correction identifier.
        /// </summary>
        public string Identifier { get; }

        /// <summary>
        /// Gets the identifier of the characteristic to which the correction relates.
        /// </summary>
        public string CharacteristicIdentifier { get; }

        /// <summary>
        /// Gets the optional deviation relative to the target.
        /// </summary>
        public GmsAnalogValue? RelativeValue { get; }

        /// <summary>
        /// Gets the optional absolute correction value.
        /// </summary>
        public GmsAnalogValue? AbsoluteValue { get; }

        /// <summary>
        /// Gets the optional lower limit from which a correction should be applied.
        /// </summary>
        public GmsAnalogValue? LowerControlLimit { get; init; }

        /// <summary>
        /// Gets the optional upper limit from which a correction should be applied.
        /// </summary>
        public GmsAnalogValue? UpperControlLimit { get; init; }

        /// <summary>
        /// Gets the optional correction description.
        /// </summary>
        public LocalizedText Description { get; init; }

        /// <summary>
        /// Gets the optional originating program name.
        /// </summary>
        public string? ProgramName { get; init; }
    }
}
