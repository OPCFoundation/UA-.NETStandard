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

using System.Collections.Generic;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// Conversions between the version-specific generated Job Control V1/V2 types
    /// and the version-neutral engine records. This type is the only boundary at
    /// which generated types are handled; the engine itself never references them.
    /// Every standard job-order and job-response field is round-tripped, including
    /// the work master, personnel, equipment, physical-asset and material
    /// collections, so that an order or response survives a same-version
    /// round-trip unchanged. Cross-version conversions are loss-aware only where
    /// the two models genuinely diverge (for example the V1 single-string
    /// descriptions and the V1-only <c>Loaded</c>/<c>Error</c> response states).
    /// </summary>
    internal static class Isa95JobControlConversions
    {
        /// <summary>
        /// Maps a canonical state to the Job Control V1 state enumeration.
        /// </summary>
        public static V1.ISA95JobOrderStateEnum ToV1State(Isa95JobCanonicalState state)
        {
            return state switch
            {
                Isa95JobCanonicalState.NotAllowedToStart => V1.ISA95JobOrderStateEnum.Waiting,
                Isa95JobCanonicalState.AllowedToStart => V1.ISA95JobOrderStateEnum.Ready,
                Isa95JobCanonicalState.Loaded => V1.ISA95JobOrderStateEnum.Loaded,
                Isa95JobCanonicalState.Running => V1.ISA95JobOrderStateEnum.Running,
                Isa95JobCanonicalState.Held => V1.ISA95JobOrderStateEnum.Held,
                Isa95JobCanonicalState.Suspended => V1.ISA95JobOrderStateEnum.Suspended,
                Isa95JobCanonicalState.Completed => V1.ISA95JobOrderStateEnum.Completed,
                Isa95JobCanonicalState.Aborted => V1.ISA95JobOrderStateEnum.Aborted,
                Isa95JobCanonicalState.Closed => V1.ISA95JobOrderStateEnum.Closed,
                Isa95JobCanonicalState.Error => V1.ISA95JobOrderStateEnum.Error,
                _ => V1.ISA95JobOrderStateEnum.Undefined
            };
        }

        /// <summary>
        /// Maps a Job Control V1 state enumeration to a canonical state. The V1-only
        /// <c>Loaded</c> and <c>Error</c> states are preserved rather than collapsed
        /// so that a V1 response round-trips unchanged.
        /// </summary>
        public static Isa95JobCanonicalState FromV1State(V1.ISA95JobOrderStateEnum state)
        {
            return state switch
            {
                V1.ISA95JobOrderStateEnum.Waiting => Isa95JobCanonicalState.NotAllowedToStart,
                V1.ISA95JobOrderStateEnum.Ready => Isa95JobCanonicalState.AllowedToStart,
                V1.ISA95JobOrderStateEnum.Loaded => Isa95JobCanonicalState.Loaded,
                V1.ISA95JobOrderStateEnum.Running => Isa95JobCanonicalState.Running,
                V1.ISA95JobOrderStateEnum.Held => Isa95JobCanonicalState.Held,
                V1.ISA95JobOrderStateEnum.Suspended => Isa95JobCanonicalState.Suspended,
                V1.ISA95JobOrderStateEnum.Completed => Isa95JobCanonicalState.Completed,
                V1.ISA95JobOrderStateEnum.Aborted => Isa95JobCanonicalState.Aborted,
                V1.ISA95JobOrderStateEnum.Closed => Isa95JobCanonicalState.Closed,
                V1.ISA95JobOrderStateEnum.Error => Isa95JobCanonicalState.Error,
                _ => Isa95JobCanonicalState.NotAllowedToStart
            };
        }

        /// <summary>
        /// Builds the Job Control V2 state array for a canonical state.
        /// </summary>
        public static ArrayOf<V2.ISA95StateDataType> ToV2StateArray(Isa95JobCanonicalState state)
        {
            return Isa95V2StateMachine.ToStateArray(state);
        }

        /// <summary>
        /// Derives a canonical state from a Job Control V2 state array.
        /// </summary>
        public static Isa95JobCanonicalState FromV2StateArray(ArrayOf<V2.ISA95StateDataType> state)
        {
            return Isa95V2StateMachine.FromStateArray(state);
        }

        /// <summary>
        /// Maps a Job Control V1 command to a neutral operation. Returns
        /// <c>false</c> for the undefined or an unrecognized command. The V1
        /// <c>Stop</c> command maps to <see cref="Isa95JobOperation.StopAndRemove"/>
        /// because V1 requires the stored information to be removed on stop.
        /// </summary>
        public static bool TryMapV1Command(V1.ISA95JobOrderCommandEnum command, out Isa95JobOperation operation)
        {
            switch (command)
            {
                case V1.ISA95JobOrderCommandEnum.Store:
                    operation = Isa95JobOperation.Store;
                    return true;
                case V1.ISA95JobOrderCommandEnum.StoreAndStart:
                    operation = Isa95JobOperation.StoreAndStart;
                    return true;
                case V1.ISA95JobOrderCommandEnum.Start:
                    operation = Isa95JobOperation.Start;
                    return true;
                case V1.ISA95JobOrderCommandEnum.Update:
                    operation = Isa95JobOperation.Update;
                    return true;
                case V1.ISA95JobOrderCommandEnum.Stop:
                    operation = Isa95JobOperation.StopAndRemove;
                    return true;
                case V1.ISA95JobOrderCommandEnum.Cancel:
                    operation = Isa95JobOperation.Cancel;
                    return true;
                case V1.ISA95JobOrderCommandEnum.Clear:
                    operation = Isa95JobOperation.Clear;
                    return true;
                default:
                    operation = default;
                    return false;
            }
        }

        /// <summary>
        /// Maps a Job Control V2 operation to a neutral operation.
        /// </summary>
        public static Isa95JobOperation MapV2Operation(Isa95JobOrderOperationV2 operation)
        {
            return operation switch
            {
                Isa95JobOrderOperationV2.Store => Isa95JobOperation.Store,
                Isa95JobOrderOperationV2.StoreAndStart => Isa95JobOperation.StoreAndStart,
                Isa95JobOrderOperationV2.Start => Isa95JobOperation.Start,
                Isa95JobOrderOperationV2.Update => Isa95JobOperation.Update,
                Isa95JobOrderOperationV2.Stop => Isa95JobOperation.Stop,
                Isa95JobOrderOperationV2.Cancel => Isa95JobOperation.Cancel,
                Isa95JobOrderOperationV2.Clear => Isa95JobOperation.Clear,
                Isa95JobOrderOperationV2.Pause => Isa95JobOperation.Pause,
                Isa95JobOrderOperationV2.Resume => Isa95JobOperation.Resume,
                Isa95JobOrderOperationV2.Abort => Isa95JobOperation.Abort,
                Isa95JobOrderOperationV2.RevokeStart => Isa95JobOperation.RevokeStart,
                _ => Isa95JobOperation.Store
            };
        }

        /// <summary>
        /// Projects a Job Control V1 job order onto a neutral job order.
        /// </summary>
        public static Isa95JobOrder FromV1Order(V1.ISA95JobOrderDataType order)
        {
            return new Isa95JobOrder
            {
                Id = order.ID ?? string.Empty,
                Description = FromStringDescription(order.Description),
                WorkMasters = FromV1WorkMasters(order.WorkMasterID),
                Priority = order.Priority,
                StartTime = order.StartTime,
                EndTime = order.EndTime,
                Parameters = FromV1Parameters(order.JobOrderParameters),
                PersonnelRequirements = FromV1Personnel(order.PersonnelRequirements),
                EquipmentRequirements = FromV1Equipment(order.EquipmentRequirements),
                PhysicalAssetRequirements = FromV1PhysicalAssets(order.PhysicalAssetRequirements),
                MaterialRequirements = FromV1Materials(order.MaterialRequirements)
            };
        }

        /// <summary>
        /// Projects a Job Control V2 job order onto a neutral job order.
        /// </summary>
        public static Isa95JobOrder FromV2Order(V2.ISA95JobOrderDataType order)
        {
            return new Isa95JobOrder
            {
                Id = order.JobOrderID ?? string.Empty,
                Description = order.Description,
                WorkMasters = FromV2WorkMasters(order.WorkMasterID),
                Priority = order.Priority,
                StartTime = order.StartTime,
                EndTime = order.EndTime,
                Parameters = FromV2Parameters(order.JobOrderParameters),
                PersonnelRequirements = FromV2Personnel(order.PersonnelRequirements),
                EquipmentRequirements = FromV2Equipment(order.EquipmentRequirements),
                PhysicalAssetRequirements = FromV2PhysicalAssets(order.PhysicalAssetRequirements),
                MaterialRequirements = FromV2Materials(order.MaterialRequirements)
            };
        }

        /// <summary>
        /// Projects a Job Control V1 job response onto a neutral job response. The
        /// <see cref="Isa95JobResponse.ReceivedAt"/> field is left at its default
        /// and set by the engine.
        /// </summary>
        public static Isa95JobResponse FromV1Response(V1.ISA95JobResponseDataType response)
        {
            return new Isa95JobResponse
            {
                Id = response.ID ?? string.Empty,
                JobOrderId = response.JobOrderID ?? string.Empty,
                Description = FromStringDescription(response.Description),
                StartTime = response.StartTime,
                EndTime = response.EndTime,
                State = FromV1State(response.JobState),
                ResponseData = FromV1Parameters(response.JobResponseData),
                PersonnelActuals = FromV1Personnel(response.PersonnelActuals),
                EquipmentActuals = FromV1Equipment(response.EquipmentActuals),
                PhysicalAssetActuals = FromV1PhysicalAssets(response.PhysicalAssetActuals),
                MaterialActuals = FromV1Materials(response.MaterialActuals)
            };
        }

        /// <summary>
        /// Projects a Job Control V2 job response onto a neutral job response. The
        /// <see cref="Isa95JobResponse.ReceivedAt"/> field is left at its default
        /// and set by the engine.
        /// </summary>
        public static Isa95JobResponse FromV2Response(V2.ISA95JobResponseDataType response)
        {
            return new Isa95JobResponse
            {
                Id = response.JobResponseID ?? string.Empty,
                JobOrderId = response.JobOrderID ?? string.Empty,
                Description = FromLocalizedTextDescription(response.Description),
                StartTime = response.StartTime,
                EndTime = response.EndTime,
                State = FromV2StateArray(response.JobState),
                ResponseData = FromV2Parameters(response.JobResponseData),
                PersonnelActuals = FromV2Personnel(response.PersonnelActuals),
                EquipmentActuals = FromV2Equipment(response.EquipmentActuals),
                PhysicalAssetActuals = FromV2PhysicalAssets(response.PhysicalAssetActuals),
                MaterialActuals = FromV2Materials(response.MaterialActuals)
            };
        }

        /// <summary>
        /// Materializes a neutral job response as a Job Control V1 job response.
        /// </summary>
        public static V1.ISA95JobResponseDataType ToV1Response(Isa95JobResponse response)
        {
            return new V1.ISA95JobResponseDataType
            {
                ID = response.Id,
                JobOrderID = response.JobOrderId,
                Description = FirstText(response.Description),
                StartTime = response.StartTime,
                EndTime = response.EndTime,
                JobState = ToV1State(response.State),
                JobResponseData = ToV1Parameters(response.ResponseData),
                PersonnelActuals = ToV1Personnel(response.PersonnelActuals),
                EquipmentActuals = ToV1Equipment(response.EquipmentActuals),
                PhysicalAssetActuals = ToV1PhysicalAssets(response.PhysicalAssetActuals),
                MaterialActuals = ToV1Materials(response.MaterialActuals)
            };
        }

        /// <summary>
        /// Materializes a neutral job response as a Job Control V2 job response.
        /// </summary>
        public static V2.ISA95JobResponseDataType ToV2Response(Isa95JobResponse response)
        {
            var result = new V2.ISA95JobResponseDataType
            {
                JobResponseID = response.Id,
                JobOrderID = response.JobOrderId,
                Description = FirstLocalizedText(response.Description),
                StartTime = response.StartTime,
                EndTime = response.EndTime,
                JobState = ToV2StateArray(response.State),
                JobResponseData = ToV2Parameters(response.ResponseData),
                PersonnelActuals = ToV2Personnel(response.PersonnelActuals),
                EquipmentActuals = ToV2Equipment(response.EquipmentActuals),
                PhysicalAssetActuals = ToV2PhysicalAssets(response.PhysicalAssetActuals),
                MaterialActuals = ToV2Materials(response.MaterialActuals)
            };
            uint mask = 0;
            if (response.Description.Count > 0)
            {
                mask |= (uint)V2.ISA95JobResponseDataTypeFields.Description;
            }
            if (!response.StartTime.Equals(DateTimeUtc.MinValue))
            {
                mask |= (uint)V2.ISA95JobResponseDataTypeFields.StartTime;
            }
            if (!response.EndTime.Equals(DateTimeUtc.MinValue))
            {
                mask |= (uint)V2.ISA95JobResponseDataTypeFields.EndTime;
            }
            if (response.ResponseData.Count > 0)
            {
                mask |= (uint)V2.ISA95JobResponseDataTypeFields.JobResponseData;
            }
            if (response.PersonnelActuals.Count > 0)
            {
                mask |= (uint)V2.ISA95JobResponseDataTypeFields.PersonnelActuals;
            }
            if (response.EquipmentActuals.Count > 0)
            {
                mask |= (uint)V2.ISA95JobResponseDataTypeFields.EquipmentActuals;
            }
            if (response.PhysicalAssetActuals.Count > 0)
            {
                mask |= (uint)V2.ISA95JobResponseDataTypeFields.PhysicalAssetActuals;
            }
            if (response.MaterialActuals.Count > 0)
            {
                mask |= (uint)V2.ISA95JobResponseDataTypeFields.MaterialActuals;
            }
            result.EncodingMask = mask;
            return result;
        }

        /// <summary>
        /// Materializes a neutral job order as a Job Control V2 job order.
        /// </summary>
        public static V2.ISA95JobOrderDataType ToV2Order(Isa95JobOrder order)
        {
            var result = new V2.ISA95JobOrderDataType
            {
                JobOrderID = order.Id,
                Description = order.Description,
                WorkMasterID = ToV2WorkMasters(order.WorkMasters),
                Priority = order.Priority,
                StartTime = order.StartTime,
                EndTime = order.EndTime,
                JobOrderParameters = ToV2Parameters(order.Parameters),
                PersonnelRequirements = ToV2Personnel(order.PersonnelRequirements),
                EquipmentRequirements = ToV2Equipment(order.EquipmentRequirements),
                PhysicalAssetRequirements = ToV2PhysicalAssets(order.PhysicalAssetRequirements),
                MaterialRequirements = ToV2Materials(order.MaterialRequirements)
            };
            uint mask = 0;
            if (order.Description.Count > 0)
            {
                mask |= (uint)V2.ISA95JobOrderDataTypeFields.Description;
            }
            if (order.WorkMasters.Count > 0)
            {
                mask |= (uint)V2.ISA95JobOrderDataTypeFields.WorkMasterID;
            }
            if (!order.StartTime.Equals(DateTimeUtc.MinValue))
            {
                mask |= (uint)V2.ISA95JobOrderDataTypeFields.StartTime;
            }
            if (!order.EndTime.Equals(DateTimeUtc.MinValue))
            {
                mask |= (uint)V2.ISA95JobOrderDataTypeFields.EndTime;
            }
            if (order.Priority != 0)
            {
                mask |= (uint)V2.ISA95JobOrderDataTypeFields.Priority;
            }
            if (order.Parameters.Count > 0)
            {
                mask |= (uint)V2.ISA95JobOrderDataTypeFields.JobOrderParameters;
            }
            if (order.PersonnelRequirements.Count > 0)
            {
                mask |= (uint)V2.ISA95JobOrderDataTypeFields.PersonnelRequirements;
            }
            if (order.EquipmentRequirements.Count > 0)
            {
                mask |= (uint)V2.ISA95JobOrderDataTypeFields.EquipmentRequirements;
            }
            if (order.PhysicalAssetRequirements.Count > 0)
            {
                mask |= (uint)V2.ISA95JobOrderDataTypeFields.PhysicalAssetRequirements;
            }
            if (order.MaterialRequirements.Count > 0)
            {
                mask |= (uint)V2.ISA95JobOrderDataTypeFields.MaterialRequirements;
            }
            result.EncodingMask = mask;
            return result;
        }

        /// <summary>
        /// Materializes a neutral job order as a Job Control V1 job order.
        /// </summary>
        public static V1.ISA95JobOrderDataType ToV1Order(Isa95JobOrder order)
        {
            return new V1.ISA95JobOrderDataType
            {
                ID = order.Id,
                Description = FirstText(order.Description),
                WorkMasterID = ToV1WorkMasters(order.WorkMasters),
                Priority = order.Priority,
                StartTime = order.StartTime,
                EndTime = order.EndTime,
                JobOrderParameters = ToV1Parameters(order.Parameters),
                PersonnelRequirements = ToV1Personnel(order.PersonnelRequirements),
                EquipmentRequirements = ToV1Equipment(order.EquipmentRequirements),
                PhysicalAssetRequirements = ToV1PhysicalAssets(order.PhysicalAssetRequirements),
                MaterialRequirements = ToV1Materials(order.MaterialRequirements)
            };
        }

        private static ArrayOf<Isa95Parameter> FromV1Parameters(ArrayOf<V1.ISA95ParameterDataType> parameters)
        {
            if (parameters.Count == 0)
            {
                return [];
            }
            var result = new List<Isa95Parameter>(parameters.Count);
            foreach (V1.ISA95ParameterDataType parameter in parameters)
            {
                result.Add(new Isa95Parameter
                {
                    Id = parameter.ID,
                    Value = parameter.Value,
                    Description = FromStringDescription(parameter.Description),
                    EngineeringUnits = FromV1Unit(parameter.UoM),
                    Subparameters = FromV1Parameters(parameter.Subparameters)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<Isa95Parameter> FromV2Parameters(ArrayOf<V2.ISA95ParameterDataType> parameters)
        {
            if (parameters.Count == 0)
            {
                return [];
            }
            var result = new List<Isa95Parameter>(parameters.Count);
            foreach (V2.ISA95ParameterDataType parameter in parameters)
            {
                result.Add(new Isa95Parameter
                {
                    Id = parameter.ID,
                    Value = parameter.Value,
                    Description = parameter.Description,
                    EngineeringUnits = FromV2Unit(parameter.EngineeringUnits),
                    Subparameters = FromV2Parameters(parameter.Subparameters)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<V1.ISA95ParameterDataType> ToV1Parameters(ArrayOf<Isa95Parameter> parameters)
        {
            if (parameters.Count == 0)
            {
                return [];
            }
            var result = new List<V1.ISA95ParameterDataType>(parameters.Count);
            foreach (Isa95Parameter parameter in parameters)
            {
                result.Add(new V1.ISA95ParameterDataType
                {
                    ID = parameter.Id,
                    Value = parameter.Value,
                    Description = FirstText(parameter.Description),
                    UoM = ToV1Unit(parameter.EngineeringUnits),
                    Subparameters = ToV1Parameters(parameter.Subparameters)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<V2.ISA95ParameterDataType> ToV2Parameters(ArrayOf<Isa95Parameter> parameters)
        {
            if (parameters.Count == 0)
            {
                return [];
            }
            var result = new List<V2.ISA95ParameterDataType>(parameters.Count);
            foreach (Isa95Parameter parameter in parameters)
            {
                uint mask = 0;
                if (parameter.Description.Count > 0)
                {
                    mask |= (uint)V2.ISA95ParameterDataTypeFields.Description;
                }
                if (!IsEmptyUnit(parameter.EngineeringUnits))
                {
                    mask |= (uint)V2.ISA95ParameterDataTypeFields.EngineeringUnits;
                }
                if (parameter.Subparameters.Count > 0)
                {
                    mask |= (uint)V2.ISA95ParameterDataTypeFields.Subparameters;
                }
                result.Add(new V2.ISA95ParameterDataType
                {
                    ID = parameter.Id,
                    Value = parameter.Value,
                    Description = parameter.Description,
                    EngineeringUnits = ToV2Unit(parameter.EngineeringUnits),
                    Subparameters = ToV2Parameters(parameter.Subparameters),
                    EncodingMask = mask
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<Isa95Property> FromV1Properties(ArrayOf<V1.ISA95PropertyDataType> properties)
        {
            if (properties.Count == 0)
            {
                return [];
            }
            var result = new List<Isa95Property>(properties.Count);
            foreach (V1.ISA95PropertyDataType property in properties)
            {
                result.Add(new Isa95Property
                {
                    Id = property.ID,
                    Value = property.Value,
                    Description = FromStringDescription(property.Description),
                    EngineeringUnits = FromV1Unit(property.UoM),
                    Subproperties = FromV1Properties(property.Subproperties)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<Isa95Property> FromV2Properties(ArrayOf<V2.ISA95PropertyDataType> properties)
        {
            if (properties.Count == 0)
            {
                return [];
            }
            var result = new List<Isa95Property>(properties.Count);
            foreach (V2.ISA95PropertyDataType property in properties)
            {
                result.Add(new Isa95Property
                {
                    Id = property.ID,
                    Value = property.Value,
                    Description = property.Description,
                    EngineeringUnits = FromV2Unit(property.EngineeringUnits),
                    Subproperties = FromV2Properties(property.Subproperties)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<V1.ISA95PropertyDataType> ToV1Properties(ArrayOf<Isa95Property> properties)
        {
            if (properties.Count == 0)
            {
                return [];
            }
            var result = new List<V1.ISA95PropertyDataType>(properties.Count);
            foreach (Isa95Property property in properties)
            {
                result.Add(new V1.ISA95PropertyDataType
                {
                    ID = property.Id,
                    Value = property.Value,
                    Description = FirstText(property.Description),
                    UoM = ToV1Unit(property.EngineeringUnits),
                    Subproperties = ToV1Properties(property.Subproperties)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<V2.ISA95PropertyDataType> ToV2Properties(ArrayOf<Isa95Property> properties)
        {
            if (properties.Count == 0)
            {
                return [];
            }
            var result = new List<V2.ISA95PropertyDataType>(properties.Count);
            foreach (Isa95Property property in properties)
            {
                uint mask = 0;
                if (property.Description.Count > 0)
                {
                    mask |= (uint)V2.ISA95PropertyDataTypeFields.Description;
                }
                if (!IsEmptyUnit(property.EngineeringUnits))
                {
                    mask |= (uint)V2.ISA95PropertyDataTypeFields.EngineeringUnits;
                }
                if (property.Subproperties.Count > 0)
                {
                    mask |= (uint)V2.ISA95PropertyDataTypeFields.Subproperties;
                }
                result.Add(new V2.ISA95PropertyDataType
                {
                    ID = property.Id,
                    Value = property.Value,
                    Description = property.Description,
                    EngineeringUnits = ToV2Unit(property.EngineeringUnits),
                    Subproperties = ToV2Properties(property.Subproperties),
                    EncodingMask = mask
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<Isa95WorkMaster> FromV1WorkMasters(ArrayOf<V1.ISA95WorkMasterDataType> workMasters)
        {
            if (workMasters.Count == 0)
            {
                return [];
            }
            var result = new List<Isa95WorkMaster>(workMasters.Count);
            foreach (V1.ISA95WorkMasterDataType workMaster in workMasters)
            {
                result.Add(new Isa95WorkMaster
                {
                    Id = workMaster.ID,
                    Description = FromStringDescription(workMaster.Description),
                    Parameters = FromV1Parameters(workMaster.Parameters)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<Isa95WorkMaster> FromV2WorkMasters(ArrayOf<V2.ISA95WorkMasterDataType> workMasters)
        {
            if (workMasters.Count == 0)
            {
                return [];
            }
            var result = new List<Isa95WorkMaster>(workMasters.Count);
            foreach (V2.ISA95WorkMasterDataType workMaster in workMasters)
            {
                result.Add(new Isa95WorkMaster
                {
                    Id = workMaster.ID,
                    Description = FromLocalizedTextDescription(workMaster.Description),
                    Parameters = FromV2Parameters(workMaster.Parameters)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<V1.ISA95WorkMasterDataType> ToV1WorkMasters(ArrayOf<Isa95WorkMaster> workMasters)
        {
            if (workMasters.Count == 0)
            {
                return [];
            }
            var result = new List<V1.ISA95WorkMasterDataType>(workMasters.Count);
            foreach (Isa95WorkMaster workMaster in workMasters)
            {
                result.Add(new V1.ISA95WorkMasterDataType
                {
                    ID = workMaster.Id,
                    Description = FirstText(workMaster.Description),
                    Parameters = ToV1Parameters(workMaster.Parameters)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<V2.ISA95WorkMasterDataType> ToV2WorkMasters(ArrayOf<Isa95WorkMaster> workMasters)
        {
            if (workMasters.Count == 0)
            {
                return [];
            }
            var result = new List<V2.ISA95WorkMasterDataType>(workMasters.Count);
            foreach (Isa95WorkMaster workMaster in workMasters)
            {
                uint mask = 0;
                if (workMaster.Description.Count > 0)
                {
                    mask |= (uint)V2.ISA95WorkMasterDataTypeFields.Description;
                }
                if (workMaster.Parameters.Count > 0)
                {
                    mask |= (uint)V2.ISA95WorkMasterDataTypeFields.Parameters;
                }
                result.Add(new V2.ISA95WorkMasterDataType
                {
                    ID = workMaster.Id,
                    Description = FirstLocalizedText(workMaster.Description),
                    Parameters = ToV2Parameters(workMaster.Parameters),
                    EncodingMask = mask
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<Isa95ResourceRequirement> FromV1Personnel(ArrayOf<V1.ISA95PersonnelDataType> resources)
        {
            if (resources.Count == 0)
            {
                return [];
            }
            var result = new List<Isa95ResourceRequirement>(resources.Count);
            foreach (V1.ISA95PersonnelDataType resource in resources)
            {
                result.Add(new Isa95ResourceRequirement
                {
                    Id = resource.ID,
                    Description = FromStringDescription(resource.Description),
                    Use = resource.PersonnelUse,
                    Quantity = resource.Quantity,
                    EngineeringUnits = FromV1Unit(resource.UoM),
                    Properties = FromV1Properties(resource.Properties)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<Isa95ResourceRequirement> FromV2Personnel(ArrayOf<V2.ISA95PersonnelDataType> resources)
        {
            if (resources.Count == 0)
            {
                return [];
            }
            var result = new List<Isa95ResourceRequirement>(resources.Count);
            foreach (V2.ISA95PersonnelDataType resource in resources)
            {
                result.Add(new Isa95ResourceRequirement
                {
                    Id = resource.ID,
                    Description = resource.Description,
                    Use = resource.PersonnelUse,
                    Quantity = resource.Quantity,
                    EngineeringUnits = FromV2Unit(resource.EngineeringUnits),
                    Properties = FromV2Properties(resource.Properties)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<V1.ISA95PersonnelDataType> ToV1Personnel(ArrayOf<Isa95ResourceRequirement> resources)
        {
            if (resources.Count == 0)
            {
                return [];
            }
            var result = new List<V1.ISA95PersonnelDataType>(resources.Count);
            foreach (Isa95ResourceRequirement resource in resources)
            {
                result.Add(new V1.ISA95PersonnelDataType
                {
                    ID = resource.Id,
                    Description = FirstText(resource.Description),
                    PersonnelUse = resource.Use,
                    Quantity = resource.Quantity,
                    UoM = ToV1Unit(resource.EngineeringUnits),
                    Properties = ToV1Properties(resource.Properties)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<V2.ISA95PersonnelDataType> ToV2Personnel(ArrayOf<Isa95ResourceRequirement> resources)
        {
            if (resources.Count == 0)
            {
                return [];
            }
            var result = new List<V2.ISA95PersonnelDataType>(resources.Count);
            foreach (Isa95ResourceRequirement resource in resources)
            {
                uint mask = 0;
                if (resource.Description.Count > 0)
                {
                    mask |= (uint)V2.ISA95PersonnelDataTypeFields.Description;
                }
                if (!string.IsNullOrEmpty(resource.Use))
                {
                    mask |= (uint)V2.ISA95PersonnelDataTypeFields.PersonnelUse;
                }
                if (!string.IsNullOrEmpty(resource.Quantity))
                {
                    mask |= (uint)V2.ISA95PersonnelDataTypeFields.Quantity;
                }
                if (!IsEmptyUnit(resource.EngineeringUnits))
                {
                    mask |= (uint)V2.ISA95PersonnelDataTypeFields.EngineeringUnits;
                }
                if (resource.Properties.Count > 0)
                {
                    mask |= (uint)V2.ISA95PersonnelDataTypeFields.Properties;
                }
                result.Add(new V2.ISA95PersonnelDataType
                {
                    ID = resource.Id,
                    Description = resource.Description,
                    PersonnelUse = resource.Use,
                    Quantity = resource.Quantity,
                    EngineeringUnits = ToV2Unit(resource.EngineeringUnits),
                    Properties = ToV2Properties(resource.Properties),
                    EncodingMask = mask
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<Isa95ResourceRequirement> FromV1Equipment(ArrayOf<V1.ISA95EquipmentDataType> resources)
        {
            if (resources.Count == 0)
            {
                return [];
            }
            var result = new List<Isa95ResourceRequirement>(resources.Count);
            foreach (V1.ISA95EquipmentDataType resource in resources)
            {
                result.Add(new Isa95ResourceRequirement
                {
                    Id = resource.ID,
                    Description = FromStringDescription(resource.Description),
                    Use = resource.EquipmentUse,
                    Quantity = resource.Quantity,
                    EngineeringUnits = FromV1Unit(resource.UoM),
                    Properties = FromV1Properties(resource.Properties)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<Isa95ResourceRequirement> FromV2Equipment(ArrayOf<V2.ISA95EquipmentDataType> resources)
        {
            if (resources.Count == 0)
            {
                return [];
            }
            var result = new List<Isa95ResourceRequirement>(resources.Count);
            foreach (V2.ISA95EquipmentDataType resource in resources)
            {
                result.Add(new Isa95ResourceRequirement
                {
                    Id = resource.ID,
                    Description = resource.Description,
                    Use = resource.EquipmentUse,
                    Quantity = resource.Quantity,
                    EngineeringUnits = FromV2Unit(resource.EngineeringUnits),
                    Properties = FromV2Properties(resource.Properties)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<V1.ISA95EquipmentDataType> ToV1Equipment(ArrayOf<Isa95ResourceRequirement> resources)
        {
            if (resources.Count == 0)
            {
                return [];
            }
            var result = new List<V1.ISA95EquipmentDataType>(resources.Count);
            foreach (Isa95ResourceRequirement resource in resources)
            {
                result.Add(new V1.ISA95EquipmentDataType
                {
                    ID = resource.Id,
                    Description = FirstText(resource.Description),
                    EquipmentUse = resource.Use,
                    Quantity = resource.Quantity,
                    UoM = ToV1Unit(resource.EngineeringUnits),
                    Properties = ToV1Properties(resource.Properties)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<V2.ISA95EquipmentDataType> ToV2Equipment(ArrayOf<Isa95ResourceRequirement> resources)
        {
            if (resources.Count == 0)
            {
                return [];
            }
            var result = new List<V2.ISA95EquipmentDataType>(resources.Count);
            foreach (Isa95ResourceRequirement resource in resources)
            {
                uint mask = 0;
                if (resource.Description.Count > 0)
                {
                    mask |= (uint)V2.ISA95EquipmentDataTypeFields.Description;
                }
                if (!string.IsNullOrEmpty(resource.Use))
                {
                    mask |= (uint)V2.ISA95EquipmentDataTypeFields.EquipmentUse;
                }
                if (!string.IsNullOrEmpty(resource.Quantity))
                {
                    mask |= (uint)V2.ISA95EquipmentDataTypeFields.Quantity;
                }
                if (!IsEmptyUnit(resource.EngineeringUnits))
                {
                    mask |= (uint)V2.ISA95EquipmentDataTypeFields.EngineeringUnits;
                }
                if (resource.Properties.Count > 0)
                {
                    mask |= (uint)V2.ISA95EquipmentDataTypeFields.Properties;
                }
                result.Add(new V2.ISA95EquipmentDataType
                {
                    ID = resource.Id,
                    Description = resource.Description,
                    EquipmentUse = resource.Use,
                    Quantity = resource.Quantity,
                    EngineeringUnits = ToV2Unit(resource.EngineeringUnits),
                    Properties = ToV2Properties(resource.Properties),
                    EncodingMask = mask
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<Isa95ResourceRequirement> FromV1PhysicalAssets(
            ArrayOf<V1.ISA95PhysicalAssetDataType> resources)
        {
            if (resources.Count == 0)
            {
                return [];
            }
            var result = new List<Isa95ResourceRequirement>(resources.Count);
            foreach (V1.ISA95PhysicalAssetDataType resource in resources)
            {
                result.Add(new Isa95ResourceRequirement
                {
                    Id = resource.ID,
                    Description = FromStringDescription(resource.Description),
                    Use = resource.PhysicalAssetUse,
                    Quantity = resource.Quantity,
                    EngineeringUnits = FromV1Unit(resource.UoM),
                    Properties = FromV1Properties(resource.Properties)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<Isa95ResourceRequirement> FromV2PhysicalAssets(
            ArrayOf<V2.ISA95PhysicalAssetDataType> resources)
        {
            if (resources.Count == 0)
            {
                return [];
            }
            var result = new List<Isa95ResourceRequirement>(resources.Count);
            foreach (V2.ISA95PhysicalAssetDataType resource in resources)
            {
                result.Add(new Isa95ResourceRequirement
                {
                    Id = resource.ID,
                    Description = resource.Description,
                    Use = resource.PhysicalAssetUse,
                    Quantity = resource.Quantity,
                    EngineeringUnits = FromV2Unit(resource.EngineeringUnits),
                    Properties = FromV2Properties(resource.Properties)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<V1.ISA95PhysicalAssetDataType> ToV1PhysicalAssets(
            ArrayOf<Isa95ResourceRequirement> resources)
        {
            if (resources.Count == 0)
            {
                return [];
            }
            var result = new List<V1.ISA95PhysicalAssetDataType>(resources.Count);
            foreach (Isa95ResourceRequirement resource in resources)
            {
                result.Add(new V1.ISA95PhysicalAssetDataType
                {
                    ID = resource.Id,
                    Description = FirstText(resource.Description),
                    PhysicalAssetUse = resource.Use,
                    Quantity = resource.Quantity,
                    UoM = ToV1Unit(resource.EngineeringUnits),
                    Properties = ToV1Properties(resource.Properties)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<V2.ISA95PhysicalAssetDataType> ToV2PhysicalAssets(
            ArrayOf<Isa95ResourceRequirement> resources)
        {
            if (resources.Count == 0)
            {
                return [];
            }
            var result = new List<V2.ISA95PhysicalAssetDataType>(resources.Count);
            foreach (Isa95ResourceRequirement resource in resources)
            {
                uint mask = 0;
                if (resource.Description.Count > 0)
                {
                    mask |= (uint)V2.ISA95PhysicalAssetDataTypeFields.Description;
                }
                if (!string.IsNullOrEmpty(resource.Use))
                {
                    mask |= (uint)V2.ISA95PhysicalAssetDataTypeFields.PhysicalAssetUse;
                }
                if (!string.IsNullOrEmpty(resource.Quantity))
                {
                    mask |= (uint)V2.ISA95PhysicalAssetDataTypeFields.Quantity;
                }
                if (!IsEmptyUnit(resource.EngineeringUnits))
                {
                    mask |= (uint)V2.ISA95PhysicalAssetDataTypeFields.EngineeringUnits;
                }
                if (resource.Properties.Count > 0)
                {
                    mask |= (uint)V2.ISA95PhysicalAssetDataTypeFields.Properties;
                }
                result.Add(new V2.ISA95PhysicalAssetDataType
                {
                    ID = resource.Id,
                    Description = resource.Description,
                    PhysicalAssetUse = resource.Use,
                    Quantity = resource.Quantity,
                    EngineeringUnits = ToV2Unit(resource.EngineeringUnits),
                    Properties = ToV2Properties(resource.Properties),
                    EncodingMask = mask
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<Isa95Material> FromV1Materials(ArrayOf<V1.ISA95MaterialDataType> materials)
        {
            if (materials.Count == 0)
            {
                return [];
            }
            var result = new List<Isa95Material>(materials.Count);
            foreach (V1.ISA95MaterialDataType material in materials)
            {
                result.Add(new Isa95Material
                {
                    MaterialClassId = material.MaterialClassID,
                    MaterialDefinitionId = material.MaterialDefinitionID,
                    MaterialLotId = material.MaterialLotID,
                    MaterialSublotId = material.MaterialSublotID,
                    Description = FromStringDescription(material.Description),
                    Use = material.MaterialUse,
                    Quantity = material.Quantity,
                    EngineeringUnits = FromV1Unit(material.UoM),
                    Properties = FromV1Properties(material.Properties)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<Isa95Material> FromV2Materials(ArrayOf<V2.ISA95MaterialDataType> materials)
        {
            if (materials.Count == 0)
            {
                return [];
            }
            var result = new List<Isa95Material>(materials.Count);
            foreach (V2.ISA95MaterialDataType material in materials)
            {
                result.Add(new Isa95Material
                {
                    MaterialClassId = material.MaterialClassID,
                    MaterialDefinitionId = material.MaterialDefinitionID,
                    MaterialLotId = material.MaterialLotID,
                    MaterialSublotId = material.MaterialSublotID,
                    Description = material.Description,
                    Use = material.MaterialUse,
                    Quantity = material.Quantity,
                    EngineeringUnits = FromV2Unit(material.EngineeringUnits),
                    Properties = FromV2Properties(material.Properties)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<V1.ISA95MaterialDataType> ToV1Materials(ArrayOf<Isa95Material> materials)
        {
            if (materials.Count == 0)
            {
                return [];
            }
            var result = new List<V1.ISA95MaterialDataType>(materials.Count);
            foreach (Isa95Material material in materials)
            {
                result.Add(new V1.ISA95MaterialDataType
                {
                    MaterialClassID = material.MaterialClassId,
                    MaterialDefinitionID = material.MaterialDefinitionId,
                    MaterialLotID = material.MaterialLotId,
                    MaterialSublotID = material.MaterialSublotId,
                    Description = FirstText(material.Description),
                    MaterialUse = material.Use,
                    Quantity = material.Quantity,
                    UoM = ToV1Unit(material.EngineeringUnits),
                    Properties = ToV1Properties(material.Properties)
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<V2.ISA95MaterialDataType> ToV2Materials(ArrayOf<Isa95Material> materials)
        {
            if (materials.Count == 0)
            {
                return [];
            }
            var result = new List<V2.ISA95MaterialDataType>(materials.Count);
            foreach (Isa95Material material in materials)
            {
                uint mask = 0;
                if (!string.IsNullOrEmpty(material.MaterialClassId))
                {
                    mask |= (uint)V2.ISA95MaterialDataTypeFields.MaterialClassID;
                }
                if (!string.IsNullOrEmpty(material.MaterialDefinitionId))
                {
                    mask |= (uint)V2.ISA95MaterialDataTypeFields.MaterialDefinitionID;
                }
                if (!string.IsNullOrEmpty(material.MaterialLotId))
                {
                    mask |= (uint)V2.ISA95MaterialDataTypeFields.MaterialLotID;
                }
                if (!string.IsNullOrEmpty(material.MaterialSublotId))
                {
                    mask |= (uint)V2.ISA95MaterialDataTypeFields.MaterialSublotID;
                }
                if (material.Description.Count > 0)
                {
                    mask |= (uint)V2.ISA95MaterialDataTypeFields.Description;
                }
                if (!string.IsNullOrEmpty(material.Use))
                {
                    mask |= (uint)V2.ISA95MaterialDataTypeFields.MaterialUse;
                }
                if (!string.IsNullOrEmpty(material.Quantity))
                {
                    mask |= (uint)V2.ISA95MaterialDataTypeFields.Quantity;
                }
                if (!IsEmptyUnit(material.EngineeringUnits))
                {
                    mask |= (uint)V2.ISA95MaterialDataTypeFields.EngineeringUnits;
                }
                if (material.Properties.Count > 0)
                {
                    mask |= (uint)V2.ISA95MaterialDataTypeFields.Properties;
                }
                result.Add(new V2.ISA95MaterialDataType
                {
                    MaterialClassID = material.MaterialClassId,
                    MaterialDefinitionID = material.MaterialDefinitionId,
                    MaterialLotID = material.MaterialLotId,
                    MaterialSublotID = material.MaterialSublotId,
                    Description = material.Description,
                    MaterialUse = material.Use,
                    Quantity = material.Quantity,
                    EngineeringUnits = ToV2Unit(material.EngineeringUnits),
                    Properties = ToV2Properties(material.Properties),
                    EncodingMask = mask
                });
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<LocalizedText> FromStringDescription(string? description)
        {
            if (string.IsNullOrEmpty(description))
            {
                return [];
            }
            return new[] { new LocalizedText(description) }.ToArrayOf();
        }

        private static ArrayOf<LocalizedText> FromLocalizedTextDescription(LocalizedText description)
        {
            if (description.IsNull)
            {
                return [];
            }
            return new[] { description }.ToArrayOf();
        }

        private static string? FirstText(ArrayOf<LocalizedText> description)
        {
            return description.Count == 0 ? null : description[0].Text;
        }

        private static LocalizedText FirstLocalizedText(ArrayOf<LocalizedText> description)
        {
            return description.Count == 0 ? LocalizedText.Null : description[0];
        }

        private static EUInformation? FromV1Unit(string? unit)
        {
            if (string.IsNullOrEmpty(unit))
            {
                return null;
            }
            return new EUInformation { DisplayName = new LocalizedText(unit) };
        }

        private static EUInformation? FromV2Unit(EUInformation engineeringUnits)
        {
            return IsEmptyUnit(engineeringUnits) ? null : engineeringUnits;
        }

        private static string? ToV1Unit(EUInformation? engineeringUnits)
        {
            if (engineeringUnits == null || engineeringUnits.DisplayName.IsNull)
            {
                return null;
            }
            return engineeringUnits.DisplayName.Text;
        }

        private static EUInformation ToV2Unit(EUInformation? engineeringUnits)
        {
            return engineeringUnits ?? new EUInformation();
        }

        private static bool IsEmptyUnit(EUInformation? engineeringUnits)
        {
            return engineeringUnits == null ||
                (string.IsNullOrEmpty(engineeringUnits.NamespaceUri) &&
                    engineeringUnits.UnitId == 0 &&
                    engineeringUnits.DisplayName.IsNull &&
                    engineeringUnits.Description.IsNull);
        }
    }
}
