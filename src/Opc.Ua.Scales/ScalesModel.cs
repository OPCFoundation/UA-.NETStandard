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

namespace Opc.Ua.Scales
{
    /// <summary>
    /// The concrete OPC 40200 scale types an application can instantiate.
    /// </summary>
    /// <remarks>
    /// <c>ScaleDeviceType</c> itself is abstract (OPC 40200 §7.4), so every
    /// scale is one of these. Each has its own conformance unit and server
    /// facet (§12.2).
    /// </remarks>
    public enum ScaleKind
    {
        /// <summary><c>SimpleScaleType</c> (§7.44).</summary>
        Simple,

        /// <summary><c>LaboratoryScaleType</c> (§7.46), a simple scale.</summary>
        Laboratory,

        /// <summary><c>HopperScaleType</c> (§7.47), a simple scale.</summary>
        Hopper,

        /// <summary>
        /// <c>WeighingModuleType</c> (§7.50), a simple scale representing a
        /// weighing bridge; usually a sub-device of another scale.
        /// </summary>
        WeighingModule,

        /// <summary><c>AutomaticFillingScaleType</c> (§7.9).</summary>
        AutomaticFilling,

        /// <summary><c>CatchweigherType</c> (§7.11).</summary>
        Catchweigher,

        /// <summary><c>CheckweigherType</c> (§7.17), a catchweigher.</summary>
        Checkweigher,

        /// <summary><c>AutomaticWeightPriceLabelerType</c> (§7.14), a catchweigher.</summary>
        AutomaticWeightPriceLabeler,

        /// <summary><c>ContinuousScaleType</c> (§7.24).</summary>
        Continuous,

        /// <summary><c>LossInWeightScaleType</c> (§7.26), a continuous scale.</summary>
        LossInWeight,

        /// <summary><c>PieceCountingScaleType</c> (§7.27).</summary>
        PieceCounting,

        /// <summary><c>RecipeScaleType</c> (§7.29).</summary>
        Recipe,

        /// <summary><c>TotalizingHopperScaleType</c> (§7.42).</summary>
        TotalizingHopper,

        /// <summary><c>VehicleScaleType</c> (§7.48).</summary>
        Vehicle
    }

    /// <summary>
    /// The <c>NotificationCategory</c> values of <c>ScaleEventType</c> and
    /// <c>ScaleAlarmType</c> (OPC 40200 Annex C, Table 188).
    /// </summary>
    public enum ScaleNotificationCategory : uint
    {
        /// <summary>OTHERS.</summary>
        Others = 0,

        /// <summary>PROCESS.</summary>
        Process = 1,

        /// <summary>SYSTEM.</summary>
        System = 2,

        /// <summary>MEMORY.</summary>
        Memory = 3,

        /// <summary>COMPONENT.</summary>
        Component = 4,

        /// <summary>COMMUNICATION.</summary>
        Communication = 5,

        /// <summary>WEIGHING_MODULE.</summary>
        WeighingModule = 6,

        /// <summary>ENVIRONMENT.</summary>
        Environment = 7
    }

    /// <summary>
    /// The <c>NotificationId</c> values OPC 40200 defines (Annex C,
    /// Table 189). Vendor-specific ids shall be greater than
    /// <see cref="ScalesModel.MinimumVendorNotificationId"/>.
    /// </summary>
    public enum ScaleNotificationId : uint
    {
        /// <summary>GENERAL_OTHER_FAULT.</summary>
        GeneralOtherFault = 1,

        /// <summary>GENERAL_PROCESS_FAULT.</summary>
        GeneralProcessFault = 100,

        /// <summary>PRODUCT_DATA_FAULT.</summary>
        ProductDataFault = 101,

        /// <summary>LABEL_FAULT.</summary>
        LabelFault = 102,

        /// <summary>BAD_PACK.</summary>
        BadPack = 103,

        /// <summary>CRITICAL_EXTERNAL_FAULT.</summary>
        CriticalExternalFault = 104,

        /// <summary>REJECT_VERIFICATION_FAULT.</summary>
        RejectVerificationFault = 105,

        /// <summary>CONTAMINATION_DETECT.</summary>
        ContaminationDetect = 106,

        /// <summary>VISION_INSPECTION.</summary>
        VisionInspection = 107,

        /// <summary>CONSECUTIVE_REJECTS.</summary>
        ConsecutiveRejects = 108,

        /// <summary>EMERGENCY_STOP.</summary>
        EmergencyStop = 109,

        /// <summary>GENERAL_SYSTEM_FAULT.</summary>
        GeneralSystemFault = 200,

        /// <summary>LOGIN_FAULT.</summary>
        LoginFault = 201,

        /// <summary>GENERAL_MEMORY_FAULT.</summary>
        GeneralMemoryFault = 300,

        /// <summary>GENERAL_COMPONENT_FAULT.</summary>
        GeneralComponentFault = 400,

        /// <summary>PRINTER_FAULT.</summary>
        PrinterFault = 401,

        /// <summary>FEEDER_FAULT.</summary>
        FeederFault = 402,

        /// <summary>FEEDER_NOT_RUNNING.</summary>
        FeederNotRunning = 403,

        /// <summary>GENERAL_COMMUNICATION_FAULT.</summary>
        GeneralCommunicationFault = 500,

        /// <summary>GENERAL_WEIGHING_MODULE_FAULT.</summary>
        GeneralWeighingModuleFault = 600,

        /// <summary>
        /// OVERLOAD_FAULT: the maximum weight is exceeded and the
        /// <c>Overload</c> Property of a <c>WeightItemType</c> is true.
        /// </summary>
        OverloadFault = 601,

        /// <summary>OUT_OF_RANGE_FAULT: outside the approved range, not yet overload.</summary>
        OutOfRangeFault = 602,

        /// <summary>UNDERLOAD_FAULT: the minimum weight is gone below.</summary>
        UnderloadFault = 603,

        /// <summary>TARE_SETTING_FAULT.</summary>
        TareSettingFault = 604,

        /// <summary>ZERO_SETTING_FAULT.</summary>
        ZeroSettingFault = 605,

        /// <summary>REZERO_REQUIRED.</summary>
        RezeroRequired = 606,

        /// <summary>GENERAL_ENVIRONMENT_FAULT.</summary>
        GeneralEnvironmentFault = 700,

        /// <summary>POWER_SUPPLY_FAULT.</summary>
        PowerSupplyFault = 701,

        /// <summary>AIR_PRESSURE_FAULT.</summary>
        AirPressureFault = 702
    }

    /// <summary>
    /// Server/client-independent facts about the OPC 40200 model.
    /// </summary>
    public static class ScalesModel
    {
        /// <summary>
        /// Vendor-specific <c>NotificationId</c> values shall be greater than
        /// this (OPC 40200 §8.1.2).
        /// </summary>
        public const uint MinimumVendorNotificationId = 5000;

        /// <summary>
        /// Gets the numeric <c>ObjectType</c> id (in the Scales namespace) of
        /// a scale kind.
        /// </summary>
        /// <param name="kind">The scale kind.</param>
        public static uint ObjectTypeOf(ScaleKind kind)
        {
            return kind switch
            {
                ScaleKind.Simple => ObjectTypes.SimpleScaleType,
                ScaleKind.Laboratory => ObjectTypes.LaboratoryScaleType,
                ScaleKind.Hopper => ObjectTypes.HopperScaleType,
                ScaleKind.WeighingModule => ObjectTypes.WeighingModuleType,
                ScaleKind.AutomaticFilling => ObjectTypes.AutomaticFillingScaleType,
                ScaleKind.Catchweigher => ObjectTypes.CatchweigherType,
                ScaleKind.Checkweigher => ObjectTypes.CheckweigherType,
                ScaleKind.AutomaticWeightPriceLabeler => ObjectTypes.AutomaticWeightPriceLabelerType,
                ScaleKind.Continuous => ObjectTypes.ContinuousScaleType,
                ScaleKind.LossInWeight => ObjectTypes.LossInWeightScaleType,
                ScaleKind.PieceCounting => ObjectTypes.PieceCountingScaleType,
                ScaleKind.Recipe => ObjectTypes.RecipeScaleType,
                ScaleKind.TotalizingHopper => ObjectTypes.TotalizingHopperScaleType,
                ScaleKind.Vehicle => ObjectTypes.VehicleScaleType,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }

        /// <summary>
        /// Gets the scale kind of a numeric <c>ObjectType</c> id, or
        /// <see langword="null"/> when the id is not one of the concrete
        /// OPC 40200 scale types.
        /// </summary>
        /// <param name="objectTypeId">The numeric id in the Scales namespace.</param>
        public static ScaleKind? KindOf(uint objectTypeId)
        {
            foreach (ScaleKind kind in AllKinds)
            {
                if (ObjectTypeOf(kind) == objectTypeId)
                {
                    return kind;
                }
            }
            return null;
        }

        /// <summary>
        /// Gets every concrete scale kind.
        /// </summary>
        public static ScaleKind[] AllKinds { get; } =
        [
            ScaleKind.Simple,
            ScaleKind.Laboratory,
            ScaleKind.Hopper,
            ScaleKind.WeighingModule,
            ScaleKind.AutomaticFilling,
            ScaleKind.Catchweigher,
            ScaleKind.Checkweigher,
            ScaleKind.AutomaticWeightPriceLabeler,
            ScaleKind.Continuous,
            ScaleKind.LossInWeight,
            ScaleKind.PieceCounting,
            ScaleKind.Recipe,
            ScaleKind.TotalizingHopper,
            ScaleKind.Vehicle
        ];

        /// <summary>
        /// Gets the product type a scale kind's <c>ProductionPreset/Products</c>
        /// placeholder is narrowed to (OPC 40200 §7.x.3: "shall have
        /// instances of X or a subtype").
        /// </summary>
        /// <remarks>
        /// Laboratory, hopper and weighing-module scales inherit the
        /// <c>SimpleProductType</c> restriction of <c>SimpleScaleType</c>, and
        /// a loss-in-weight scale the <c>ContinuousProductType</c> one of
        /// <c>ContinuousScaleType</c>.
        /// </remarks>
        /// <param name="kind">The scale kind.</param>
        public static uint ProductTypeOf(ScaleKind kind)
        {
            return kind switch
            {
                ScaleKind.Simple or ScaleKind.Laboratory or ScaleKind.Hopper or ScaleKind.WeighingModule =>
                    ObjectTypes.SimpleProductType,
                ScaleKind.AutomaticFilling => ObjectTypes.AutomaticFillingProductType,
                ScaleKind.Catchweigher => ObjectTypes.CatchweigherProductType,
                ScaleKind.Checkweigher => ObjectTypes.CheckweigherProductType,
                ScaleKind.AutomaticWeightPriceLabeler => ObjectTypes.AutomaticWeightPriceLabelerProductType,
                ScaleKind.Continuous or ScaleKind.LossInWeight => ObjectTypes.ContinuousProductType,
                ScaleKind.PieceCounting => ObjectTypes.PieceCountingProductType,
                ScaleKind.Recipe => ObjectTypes.RecipeProductType,
                ScaleKind.TotalizingHopper => ObjectTypes.TotalizingHopperProductType,
                ScaleKind.Vehicle => ObjectTypes.VehicleProductType,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }

        /// <summary>
        /// Gets the category an OPC 40200 notification id belongs to.
        /// </summary>
        /// <param name="id">The notification id.</param>
        /// <returns>
        /// The category of a defined id, or <see cref="ScaleNotificationCategory.Others"/>
        /// for a vendor-specific one.
        /// </returns>
        public static ScaleNotificationCategory CategoryOf(ScaleNotificationId id)
        {
            return (uint)id switch
            {
                >= 100 and < 200 => ScaleNotificationCategory.Process,
                >= 200 and < 300 => ScaleNotificationCategory.System,
                >= 300 and < 400 => ScaleNotificationCategory.Memory,
                >= 400 and < 500 => ScaleNotificationCategory.Component,
                >= 500 and < 600 => ScaleNotificationCategory.Communication,
                >= 600 and < 700 => ScaleNotificationCategory.WeighingModule,
                >= 700 and < 800 => ScaleNotificationCategory.Environment,
                _ => ScaleNotificationCategory.Others
            };
        }

        /// <summary>
        /// Gets the <c>ValueAsText</c> of a notification category - the
        /// upper-case name Table 188 defines.
        /// </summary>
        /// <param name="category">The category.</param>
        public static string TextOf(ScaleNotificationCategory category)
        {
            return category switch
            {
                ScaleNotificationCategory.Process => "PROCESS",
                ScaleNotificationCategory.System => "SYSTEM",
                ScaleNotificationCategory.Memory => "MEMORY",
                ScaleNotificationCategory.Component => "COMPONENT",
                ScaleNotificationCategory.Communication => "COMMUNICATION",
                ScaleNotificationCategory.WeighingModule => "WEIGHING_MODULE",
                ScaleNotificationCategory.Environment => "ENVIRONMENT",
                _ => "OTHERS"
            };
        }

        /// <summary>
        /// Gets the <c>ValueAsText</c> of a notification id - the upper-case
        /// name Table 189 defines - or the decimal id for a vendor-specific
        /// one.
        /// </summary>
        /// <param name="id">The notification id.</param>
        public static string TextOf(ScaleNotificationId id)
        {
            return id switch
            {
                ScaleNotificationId.GeneralOtherFault => "GENERAL_OTHER_FAULT",
                ScaleNotificationId.GeneralProcessFault => "GENERAL_PROCESS_FAULT",
                ScaleNotificationId.ProductDataFault => "PRODUCT_DATA_FAULT",
                ScaleNotificationId.LabelFault => "LABEL_FAULT",
                ScaleNotificationId.BadPack => "BAD_PACK",
                ScaleNotificationId.CriticalExternalFault => "CRITICAL_EXTERNAL_FAULT",
                ScaleNotificationId.RejectVerificationFault => "REJECT_VERIFICATION_FAULT",
                ScaleNotificationId.ContaminationDetect => "CONTAMINATION_DETECT",
                ScaleNotificationId.VisionInspection => "VISION_INSPECTION",
                ScaleNotificationId.ConsecutiveRejects => "CONSECUTIVE_REJECTS",
                ScaleNotificationId.EmergencyStop => "EMERGENCY_STOP",
                ScaleNotificationId.GeneralSystemFault => "GENERAL_SYSTEM_FAULT",
                ScaleNotificationId.LoginFault => "LOGIN_FAULT",
                ScaleNotificationId.GeneralMemoryFault => "GENERAL_MEMORY_FAULT",
                ScaleNotificationId.GeneralComponentFault => "GENERAL_COMPONENT_FAULT",
                ScaleNotificationId.PrinterFault => "PRINTER_FAULT",
                ScaleNotificationId.FeederFault => "FEEDER_FAULT",
                ScaleNotificationId.FeederNotRunning => "FEEDER_NOT_RUNNING",
                ScaleNotificationId.GeneralCommunicationFault => "GENERAL_COMMUNICATION_FAULT",
                ScaleNotificationId.GeneralWeighingModuleFault => "GENERAL_WEIGHING_MODULE_FAULT",
                ScaleNotificationId.OverloadFault => "OVERLOAD_FAULT",
                ScaleNotificationId.OutOfRangeFault => "OUT_OF_RANGE_FAULT",
                ScaleNotificationId.UnderloadFault => "UNDERLOAD_FAULT",
                ScaleNotificationId.TareSettingFault => "TARE_SETTING_FAULT",
                ScaleNotificationId.ZeroSettingFault => "ZERO_SETTING_FAULT",
                ScaleNotificationId.RezeroRequired => "REZERO_REQUIRED",
                ScaleNotificationId.GeneralEnvironmentFault => "GENERAL_ENVIRONMENT_FAULT",
                ScaleNotificationId.PowerSupplyFault => "POWER_SUPPLY_FAULT",
                ScaleNotificationId.AirPressureFault => "AIR_PRESSURE_FAULT",
                _ => ((uint)id).ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
        }
    }

    /// <summary>
    /// The server facet (profile) URIs of OPC 40200 §12.2.2, Table 163.
    /// </summary>
    /// <remarks>
    /// The URIs are reproduced verbatim, including the inconsistencies the
    /// specification publishes: four facets lack the <c>/Server/</c> segment
    /// and three contain a space. A client compares these strings, so
    /// "correcting" them would make a conformant server unrecognisable.
    /// </remarks>
    public static class ScalesProfiles
    {
        private const string Root = "http://opcfoundation.org/UA-Profile/External/Scales/V2/";

        /// <summary>Base Scale Server Facet (Table 164).</summary>
        public const string BaseScale = Root + "Scales_Base_Scale";

        /// <summary>Scale System Server Facet (Table 165).</summary>
        public const string ScaleSystem = Root + "Scales_Scale_System";

        /// <summary>Feeder Module Server Facet (Table 166).</summary>
        public const string FeederModule = Root + "Scales_Feeder_Module";

        /// <summary>Printer Module Server Facet (Table 167).</summary>
        public const string PrinterModule = Root + "Scales_Printer_Module";

        /// <summary>Minimal Production Preset Server Facet (Table 168).</summary>
        public const string MinimalProductionPreset = Root + "Server/Scales_Minimal_Production_Preset";

        /// <summary>Full Production Preset Server Facet (Table 169).</summary>
        public const string FullProductionPreset = Root + "Server/Scales_Full_Production_Preset";

        /// <summary>International System of Units Server Facet (Table 170).</summary>
        public const string InternationalSystemOfUnits = Root + "Server/Scales_International_System_of_Units";

        /// <summary>AutomaticFillingScale Server Facet.</summary>
        public const string AutomaticFillingScale = Root + "Server/Scales_AutomaticFillingScale";

        /// <summary>Catchweigher Server Facet.</summary>
        public const string Catchweigher = Root + "Server/Scales_Catchweigher";

        /// <summary>AutomaticWeightPriceLabeler Server Facet.</summary>
        public const string AutomaticWeightPriceLabeler = Root + "Server/Scales_AutomaticWeightPriceLabeler";

        /// <summary>Checkweigher Server Facet.</summary>
        public const string Checkweigher = Root + "Server/Scales_Checkweigher";

        /// <summary>Continuous Scale Server Facet.</summary>
        public const string ContinuousScale = Root + "Server/Scales_Continuous_Scale";

        /// <summary>LossInWeight Scale Server Facet (the URI contains a space).</summary>
        public const string LossInWeightScale = Root + "Server/Scales_LossInWeight Scale";

        /// <summary>PieceCountingScale Server Facet.</summary>
        public const string PieceCountingScale = Root + "Server/Scales_PieceCountingScale";

        /// <summary>RecipeScale Server Facet.</summary>
        public const string RecipeScale = Root + "Server/Scales_RecipeScale";

        /// <summary>Totalizing Hopper Scale Server Facet (the URI contains a space).</summary>
        public const string TotalizingHopperScale = Root + "Server/Scales_Totalizing_Hopper Scale";

        /// <summary>Simple Scale Server Facet.</summary>
        public const string SimpleScale = Root + "Server/Scales_Simple_Scale";

        /// <summary>Laboratory Scale Server Facet.</summary>
        public const string LaboratoryScale = Root + "Server/Scales_Laboratory_Scale";

        /// <summary>Hopper Scale Server Facet.</summary>
        public const string HopperScale = Root + "Server/Scales_Hopper_Scale";

        /// <summary>Weighing Bridge Server Facet (the URI contains a space).</summary>
        public const string WeighingBridge = Root + "Server/Scales_Weighing Bridge";

        /// <summary>Vehicle Scale Server Facet.</summary>
        public const string VehicleScale = Root + "Server/Scales_Vehicle_Scale";

        /// <summary>
        /// Gets the facet a scale kind conforms to on top of the Base Scale
        /// facet.
        /// </summary>
        /// <param name="kind">The scale kind.</param>
        public static string FacetOf(ScaleKind kind)
        {
            return kind switch
            {
                ScaleKind.Simple => SimpleScale,
                ScaleKind.Laboratory => LaboratoryScale,
                ScaleKind.Hopper => HopperScale,
                ScaleKind.WeighingModule => WeighingBridge,
                ScaleKind.AutomaticFilling => AutomaticFillingScale,
                ScaleKind.Catchweigher => Catchweigher,
                ScaleKind.Checkweigher => Checkweigher,
                ScaleKind.AutomaticWeightPriceLabeler => AutomaticWeightPriceLabeler,
                ScaleKind.Continuous => ContinuousScale,
                ScaleKind.LossInWeight => LossInWeightScale,
                ScaleKind.PieceCounting => PieceCountingScale,
                ScaleKind.Recipe => RecipeScale,
                ScaleKind.TotalizingHopper => TotalizingHopperScale,
                ScaleKind.Vehicle => VehicleScale,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }
    }
}
