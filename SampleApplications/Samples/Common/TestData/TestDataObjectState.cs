/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Xml;
using System.IO;
using System.Reflection;
using Opc.Ua;

namespace TestData
{
    public partial class TestDataObjectState
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
        protected void InitializeVariable(ISystemContext context, BaseVariableState variable, uint numericId)
        {
            variable.NumericId = numericId;

            // provide an implementation that produces a random value on each read.
            if (SimulationActive.Value)
            {
                variable.OnReadValue = DoDeviceRead;     
            }

            // set a valid initial value.
            TestDataSystem system = context.SystemHandle as TestDataSystem;

            if (system != null)
            {
                GenerateValue(system, variable);
            }

            // allow writes if the simulation is not active.
            if (!SimulationActive.Value)
            {
                variable.AccessLevel = variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            }

            // set the EU range.
            BaseVariableState euRange = variable.FindChild(context, Opc.Ua.BrowseNames.EURange) as BaseVariableState;

            if (euRange != null)
            {
                if (context.TypeTable.IsTypeOf(variable.DataType, Opc.Ua.DataTypeIds.UInteger))
                {
                    euRange.Value = new Range(250, 50);
                }
                else
                {
                    euRange.Value = new Range(100, -100);
                }
            }
            
            variable.OnSimpleWriteValue = OnWriteAnalogValue;
        }
        
        /// <summary>
        /// Validates a written value.
        /// </summary>
        public ServiceResult OnWriteAnalogValue(
            ISystemContext context,
            NodeState node,
            ref object value)
        {
            try
            {

            BaseVariableState euRange = node.FindChild(context, Opc.Ua.BrowseNames.EURange) as BaseVariableState;

            if (euRange == null)
            {
                return ServiceResult.Good;
            }

            Range range = euRange.Value as Range;
            
            if (range == null)
            {
                return ServiceResult.Good;
            }

            Array array = value as Array;

            if (array != null)
            {
                for (int ii = 0; ii < array.Length; ii++)
                {
                    object element = array.GetValue(ii);

                    if (typeof(Variant).IsInstanceOfType(element))
                    {
                        element = ((Variant)element).Value;
                    }

                    double elementNumber = Convert.ToDouble(element);

                    if (elementNumber > range.High || elementNumber < range.Low)
                    {
                        return StatusCodes.BadOutOfRange;
                    }
                }

                return ServiceResult.Good;
            }
                        
            double number = Convert.ToDouble(value);
            
            if (number > range.High || number < range.Low)
            {
                return StatusCodes.BadOutOfRange;
            }
            
            return ServiceResult.Good;
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        /// <summary>
        /// Generates a new value for the variable.
        /// </summary>
        protected void GenerateValue(TestDataSystem system, BaseVariableState variable)
        {
            variable.Value = system.ReadValue(variable);
            variable.Timestamp = DateTime.UtcNow;
            variable.StatusCode = StatusCodes.Good;
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

                ServiceResult error = BaseVariableState.ApplyIndexRangeAndDataEncoding(
                    context,
                    indexRange,
                    dataEncoding,
                    ref value);

                if (ServiceResult.IsBad(error))
                {
                    statusCode = error.StatusCode;
                }
                
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
