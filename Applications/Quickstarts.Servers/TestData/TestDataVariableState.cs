/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;

namespace TestData
{
    public partial class TestDataVariableState
    {
        #region Initialization
        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

            GenerateValues.OnCall = OnGenerateValues;
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Initialzies the variable as a counter.
        /// </summary>
        protected void InitializeVariable(ISystemContext context, BaseVariableState variable)
        {
            // provide an implementation that produces a random value on each read.
            if (SimulationActive.Value)
            {
                variable.OnReadValue = DoDeviceRead;
            }

            // set a valid initial value.
            TestDataSystem system = context.SystemHandle as TestDataSystem;

            // allow writes if the simulation is not active.
            if (!SimulationActive.Value)
            {
                variable.AccessLevel = variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            }
        }

        /// <summary>
        /// Handles the generate values method.
        /// </summary>
        protected virtual ServiceResult OnGenerateValues(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint count)
        {
            ClearChangeMasks(context, true);

            if (AreEventsMonitored)
            {
                GenerateValuesEventState e = new GenerateValuesEventState(null);

                TranslationInfo message = new TranslationInfo(
                    "GenerateValuesEventType",
                    "en-US",
                    "New values generated for test source '{0}'.",
                    this.DisplayName);

                e.Initialize(
                    context,
                    this,
                    EventSeverity.MediumLow,
                    new LocalizedText(message));

                e.Iterations = new PropertyState<uint>(e);
                e.Iterations.Value = count;

                e.NewValueCount = new PropertyState<uint>(e);
                e.NewValueCount.Value = 10;

                ReportEvent(context, e);
            }

#if CONDITION_SAMPLES
            this.CycleComplete.RequestAcknowledgement(context, (ushort)EventSeverity.Low);
#endif

            return ServiceResult.Good;
        }

        /// <summary>
        /// Generates a new value each time the value is read.
        /// </summary>
        private ServiceResult DoDeviceRead(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            BaseVariableState variable = node as BaseVariableState;

            if (variable == null)
            {
                return ServiceResult.Good;
            }

            if (!SimulationActive.Value)
            {
                return ServiceResult.Good;
            }

            TestDataSystem system = context.SystemHandle as TestDataSystem;

            if (system == null)
            {
                return StatusCodes.BadOutOfService;
            }

            try
            {
                value = system.ReadValue(variable);

                statusCode = StatusCodes.Good;
                timestamp = DateTime.UtcNow;

                return ServiceResult.Good;
            }
            catch (Exception e)
            {
                return new ServiceResult(e);
            }
        }
        #endregion
    }
}
