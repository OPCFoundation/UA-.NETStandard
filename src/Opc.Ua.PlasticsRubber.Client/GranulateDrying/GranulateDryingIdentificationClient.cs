// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.Machinery;

namespace Opc.Ua.GranulateDrying.Client
{
    /// <summary>
    /// Typed identification values on the existing Machinery add-in, not a fictitious DI device root.
    /// </summary>
    public sealed class GranulateDryingIdentificationClient
    {
        /// <summary>
        /// Opens the generated identification add-in.
        /// </summary>
        internal GranulateDryingIdentificationClient(
            GranulateDryingClientContext context, MachineIdentificationTypeClient proxy)
        {
            m_context = context;
            Proxy = proxy;
        }

        /// <summary>
        /// Gets the generated Machinery identification proxy.
        /// </summary>
        public MachineIdentificationTypeClient Proxy { get; }

        /// <summary>
        /// Gets the mandatory manufacturer name.
        /// </summary>
        public GranulateDryingVariable<LocalizedText> Manufacturer =>
            DiValue("Manufacturer", GranulateDryingCodecs.Text);

        /// <summary>
        /// Gets the mandatory model designation.
        /// </summary>
        public GranulateDryingVariable<LocalizedText> Model => DiValue("Model", GranulateDryingCodecs.Text);

        /// <summary>
        /// Gets the mandatory serial number.
        /// </summary>
        public GranulateDryingVariable<string> SerialNumber => DiValue("SerialNumber", GranulateDryingCodecs.String);

        /// <summary>
        /// Gets the globally unique product-instance URI used by asset discovery.
        /// </summary>
        public GranulateDryingVariable<string> ProductInstanceUri =>
            DiValue("ProductInstanceUri", GranulateDryingCodecs.String);

        /// <summary>
        /// Gets the required Granulate Drying Device class label.
        /// </summary>
        public GranulateDryingVariable<string> DeviceClass => DiValue("DeviceClass", GranulateDryingCodecs.String);

        /// <summary>
        /// Gets the optional manufacturer URI.
        /// </summary>
        public GranulateDryingVariable<string> ManufacturerUri => DiValue("ManufacturerUri",
            GranulateDryingCodecs.String);

        /// <summary>
        /// Gets the optional product code.
        /// </summary>
        public GranulateDryingVariable<string> ProductCode => DiValue("ProductCode", GranulateDryingCodecs.String);

        /// <summary>
        /// Gets the optional hardware revision.
        /// </summary>
        public GranulateDryingVariable<string> HardwareRevision => DiValue("HardwareRevision",
            GranulateDryingCodecs.String);

        /// <summary>
        /// Gets the optional software revision.
        /// </summary>
        public GranulateDryingVariable<string> SoftwareRevision => DiValue("SoftwareRevision",
            GranulateDryingCodecs.String);

        /// <summary>
        /// Gets the optional operator-assigned asset identifier.
        /// </summary>
        public GranulateDryingVariable<string> AssetId => DiValue("AssetId", GranulateDryingCodecs.String, true);

        /// <summary>
        /// Gets the optional operator-assigned component name.
        /// </summary>
        public GranulateDryingVariable<LocalizedText> ComponentName =>
            DiValue("ComponentName", GranulateDryingCodecs.Text, true);

        /// <summary>
        /// Gets the optional Machinery location.
        /// </summary>
        public GranulateDryingVariable<string> Location => MachineryValue("Location", GranulateDryingCodecs.String,
            true);

        /// <summary>
        /// Gets the optional initial operation date.
        /// </summary>
        public GranulateDryingVariable<DateTimeUtc> InitialOperationDate =>
            MachineryValue("InitialOperationDate", GranulateDryingCodecs.Time);

        /// <summary>
        /// Gets the optional construction year.
        /// </summary>
        public GranulateDryingVariable<ushort> YearOfConstruction =>
            MachineryValue("YearOfConstruction", GranulateDryingCodecs.UInt16);

        /// <summary>
        /// Gets the optional construction month.
        /// </summary>
        public GranulateDryingVariable<byte> MonthOfConstruction =>
            MachineryValue("MonthOfConstruction", GranulateDryingCodecs.Byte);

        /// <summary>
        /// Creates a DI-qualified property accessor.
        /// </summary>
        /// <typeparam name="T">The declared property value type.</typeparam>
        private GranulateDryingVariable<T> DiValue<T>(
            string name, GranulateDryingCodec<T> codec, bool writable = false) =>
            m_context.Variable(Proxy.ObjectId, codec, writable,
                new GranulateDryingPathElement(Di.Namespaces.OpcUaDi, name));

        /// <summary>
        /// Creates a Machinery-qualified property accessor.
        /// </summary>
        /// <typeparam name="T">The declared property value type.</typeparam>
        private GranulateDryingVariable<T> MachineryValue<T>(
            string name, GranulateDryingCodec<T> codec, bool writable = false) =>
            m_context.Variable(Proxy.ObjectId, codec, writable,
                new GranulateDryingPathElement(Machinery.Namespaces.Machinery, name));

        /// <summary>
        /// The connected-session context.
        /// </summary>
        private readonly GranulateDryingClientContext m_context;
    }
}
