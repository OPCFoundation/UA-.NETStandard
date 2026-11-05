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

namespace Opc.Ua.IA.Server
{
    /// <summary>
    /// Builds an immutable calibration-target configuration without creating or owning nodes.
    /// </summary>
    public sealed class IaCalibrationTargetBuilder
    {
        /// <summary>
        /// Starts a configuration for a concrete calibration category and DI identification.
        /// </summary>
        public IaCalibrationTargetBuilder(
            IaCalibrationTargetCategory category,
            IaCalibrationIdentification identification)
        {
            m_category = category;
            m_identification = identification ?? throw new ArgumentNullException(nameof(identification));
        }

        /// <summary>
        /// Adds one calibration feature.
        /// </summary>
        public IaCalibrationTargetBuilder WithFeature(IaCalibrationFeature feature)
        {
            m_features.Add(feature ?? throw new ArgumentNullException(nameof(feature)));
            return this;
        }

        /// <summary>
        /// Adds one vendor-defined operational condition using a typed value or range.
        /// </summary>
        public IaCalibrationTargetBuilder WithOperationalCondition(IaCalibrationFeature condition)
        {
            m_conditions.Add(condition ?? throw new ArgumentNullException(nameof(condition)));
            return this;
        }

        /// <summary>
        /// Selects optional target properties. Reusable categories always include LastValidationDate.
        /// </summary>
        public IaCalibrationTargetBuilder WithOptionalFields(IaCalibrationTargetFields fields)
        {
            m_fields = fields;
            return this;
        }

        /// <summary>
        /// Relates a dynamic target's category to the instrument that measured the individual item.
        /// </summary>
        public IaCalibrationTargetBuilder WithReferenceMeasurementInstrument(NodeId instrument)
        {
            if (instrument.IsNull)
            {
                throw new ArgumentException("A reference instrument NodeId is required.", nameof(instrument));
            }
            m_instruments.Add(instrument);
            return this;
        }

        /// <summary>
        /// Validates and returns a detached configuration; subsequent builder changes do not affect it.
        /// </summary>
        public IaCalibrationTargetConfiguration Build()
        {
            var configuration = new IaCalibrationTargetConfiguration(
                m_category,
                m_identification,
                m_features.ToArray())
            {
                OptionalFields = m_fields,
                OperationalConditions = m_conditions.ToArray(),
                ReferenceMeasurementInstruments = m_instruments.ToArray()
            };
            IaCalibrationValidation.ValidateConfiguration(configuration);
            return configuration;
        }

        /// <summary>The selected category.</summary>
        private readonly IaCalibrationTargetCategory m_category;

        /// <summary>The immutable DI identification.</summary>
        private readonly IaCalibrationIdentification m_identification;

        /// <summary>The selected calibration features.</summary>
        private readonly List<IaCalibrationFeature> m_features = [];

        /// <summary>The selected operational conditions.</summary>
        private readonly List<IaCalibrationFeature> m_conditions = [];

        /// <summary>The related reference instruments.</summary>
        private readonly List<NodeId> m_instruments = [];

        /// <summary>The selected optional target properties.</summary>
        private IaCalibrationTargetFields m_fields;
    }
}
