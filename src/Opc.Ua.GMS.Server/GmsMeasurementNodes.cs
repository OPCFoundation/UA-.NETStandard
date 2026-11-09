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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using Opc.Ua.GMS.Contracts;

namespace Opc.Ua.GMS.Server
{
    /// <summary>
    /// Projects released characteristic and correction fields using their generated declaration factories.
    /// </summary>
    internal static class GmsMeasurementNodes
    {
        /// <summary>
        /// Populates a generated characteristic, materializing only supplied optional fields.
        /// </summary>
        internal static void SetCharacteristic(
            ISystemContext context,
            CharacteristicState state,
            GmsCharacteristic value)
        {
            if (value.Identifier != null)
            {
                state.AddCharacteristicIdentifier(context);
                state.CharacteristicIdentifier!.Value = value.Identifier;
            }
            if (value.CharacteristicsClass.HasValue)
            {
                state.AddCharacteristicsClass(context);
                state.CharacteristicsClass!.Value = value.CharacteristicsClass.Value;
            }
            if (value.Formula != null)
            {
                state.AddFormula(context);
                state.Formula!.Value = value.Formula;
            }
            if (value.IsValid.HasValue)
            {
                state.AddIsValid(context);
                state.IsValid!.Value = value.IsValid.Value;
            }
            if (value.Evaluation.HasValue)
            {
                state.AddResultEvaluation(context);
                state.ResultEvaluation!.Value = value.Evaluation.Value;
            }
            if (value.ResultValue != null)
            {
                state.AddResultValue(context);
                SetAnalog(state.ResultValue!, value.ResultValue);
            }
            if (value.Nominal != null)
            {
                state.AddNominal(context);
                SetAnalog(state.Nominal!, value.Nominal);
            }
            if (value.LowerTolerance != null)
            {
                state.AddLowerToleranceLimit(context);
                SetTolerance(context, state.LowerToleranceLimit!, value.LowerTolerance);
            }
            if (value.UpperTolerance != null)
            {
                state.AddUpperToleranceLimit(context);
                SetTolerance(context, state.UpperToleranceLimit!, value.UpperTolerance);
            }
        }

        /// <summary>
        /// Populates a generated correction without defining any correction-control method.
        /// </summary>
        internal static void SetCorrection(ISystemContext context, CorrectionState state, GmsCorrection value)
        {
            state.Identifier!.Value = value.Identifier;
            state.CharacteristicIdentifier!.Value = value.CharacteristicIdentifier;
            if (value.RelativeValue != null)
            {
                state.AddCorrectionValueRelative(context);
                SetAnalog(state.CorrectionValueRelative!, value.RelativeValue);
            }
            if (value.AbsoluteValue != null)
            {
                state.AddCorrectionValueAbsolute(context);
                SetAnalog(state.CorrectionValueAbsolute!, value.AbsoluteValue);
            }
            if (value.LowerControlLimit != null)
            {
                state.AddLowerControlLimit(context);
                SetAnalog(state.LowerControlLimit!, value.LowerControlLimit);
            }
            if (value.UpperControlLimit != null)
            {
                state.AddUpperControlLimit(context);
                SetAnalog(state.UpperControlLimit!, value.UpperControlLimit);
            }
            if (!value.Description.IsNull)
            {
                state.AddDescription(context);
                state.Description!.Value = value.Description;
            }
            if (value.ProgramName != null)
            {
                state.AddProgramName(context);
                state.ProgramName!.Value = value.ProgramName;
            }
        }

        /// <summary>
        /// Updates a sample through the Variant surface so a bad null never turns into a fictitious Double zero.
        /// </summary>
        private static void SetAnalog(AnalogUnitState<double> state, GmsAnalogValue value)
        {
            BaseVariableState variable = state;
            variable.Value = value.Value.WrappedValue;
            variable.StatusCode = value.Value.StatusCode;
            variable.Timestamp = value.Value.SourceTimestamp;
            state.EngineeringUnits!.Value = value.EngineeringUnits;
        }

        /// <summary>
        /// Retains the mandatory GMS-qualified tolerance-form discriminator on the generated analog child.
        /// </summary>
        private static void SetTolerance(ISystemContext context, AnalogUnitState<double> state, GmsTolerance value)
        {
            SetAnalog(state, value.Value);
            var name = new QualifiedName(BrowseNames.ToleranceForm,
                (ushort)context.NamespaceUris.GetIndex(GmsModel.NamespaceUri));
            if (state.FindChild(context, name) is not PropertyState<ToleranceLimitEnum> form)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, "The generated tolerance limit has no ToleranceForm.");
            }
            form.Value = value.Form;
        }
    }
}
