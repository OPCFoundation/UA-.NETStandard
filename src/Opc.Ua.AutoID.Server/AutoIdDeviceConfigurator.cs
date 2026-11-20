// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.AutoID.Server.Builders;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Instantiates the mandatory and the supported optional components of a reader device before
    /// it is registered.
    /// </summary>
    /// <remarks>
    /// The generated instance factory creates the mandatory children only; the optional components
    /// a provider supports are added here, so a reader publishes exactly the methods it can run and
    /// the registration sees the complete subtree.
    /// </remarks>
    internal static class AutoIdDeviceConfigurator
    {
        /// <summary>
        /// The <c>AutoIdModelVersion</c> OPC 30010 6.1.3.16 prescribes for the specification.
        /// </summary>
        public const string ModelVersion = "1.00";

        public static void Configure(ISystemContext context, AutoIdDeviceState device, AutoIdReaderBuilder builder)
        {
            IAutoIdReaderProvider provider = builder.Provider;
            device.EventNotifier |= EventNotifiers.SubscribeToEvents;

            // Mandatory components (OPC 30010 6.1.2).
            device.AutoIdModelVersion!.Value = ModelVersion;
            device.DeviceName!.Value = builder.DeviceName ?? builder.BrowseName.Name!;
            device.DeviceStatus!.Value = DeviceStatusEnumeration.Idle;

            // The scan result variables are the simple synchronous access (OPC 30010 6.1.3.9
            // and 6.1.3.10); they are updated by every scan, so every reader publishes them.
            device.AddLastScanData(context).AddLastScanTimestamp(context);
            device.LastScanData!.StatusCode = StatusCodes.UncertainInitialValue;
            device.LastScanTimestamp!.StatusCode = StatusCodes.UncertainInitialValue;

            if (builder.Parameters != null)
            {
                AutoIdReader.ConfigureParameters(context, device, builder.Parameters,
                    ((IAutoIdScanParametersProvider)provider).SupportedCodeTypes, builder.HasScanControl,
                    builder.ScanWritePermissions, builder.ParameterCapabilities);
                builder.ConfigureParameters?.Invoke(context, device.RuntimeParameters!);
            }
            if (builder.HasDiagnostics)
            {
                AutoIdReader.ConfigureDiagnostics(context, device);
            }
            AutoIdReader.ConfigureImages(context, device, builder.Images);
            if (builder.ConfigureIOData != null)
            {
                device.AddIOData(context);
                builder.ConfigureIOData(context, device.IOData!);
            }

            if (builder.DeviceInfo != null)
            {
                device.AddDeviceInfo(context);
                device.DeviceInfo!.Value = builder.DeviceInfo;
            }
            if (builder.DeviceLocationName != null)
            {
                device.AddDeviceLocationName(context);
                device.DeviceLocationName!.Value = builder.DeviceLocationName;
            }
            if (builder.DeviceLocation != null)
            {
                device.AddDeviceLocation(context).AddGetDeviceLocation(context);
                device.DeviceLocation!.Value = builder.DeviceLocation;
            }
            if (provider is IAutoIdAsyncScanProvider)
            {
                device.AddScanStart(context).AddScanStop(context);
            }

            bool scan = provider is IAutoIdScanProvider;
            switch (device)
            {
                case RfidReaderDeviceState rfid:
                    ConfigureRfid(context, rfid, builder, scan);
                    break;
                case OcrReaderDeviceState ocr when scan:
                    ocr.AddScan(context);
                    break;
                case OpticalVerifierDeviceState verifier when scan:
                    verifier.AddScan(context);
                    break;
                case OpticalReaderDeviceState optical when scan:
                    optical.AddScan(context);
                    break;
                case RtlsDeviceState rtls:
                    ConfigureRtls(context, rtls, builder, scan);
                    break;
            }
        }

        private static void ConfigureRfid(
            ISystemContext context,
            RfidReaderDeviceState rfid,
            AutoIdReaderBuilder builder,
            bool scan)
        {
            if (scan)
            {
                rfid.AddScan(context);
            }
            rfid.AddLastScanAntenna(context).AddLastScanRSSI(context);
            rfid.LastScanAntenna!.StatusCode = StatusCodes.UncertainInitialValue;
            rfid.LastScanRSSI!.StatusCode = StatusCodes.UncertainInitialValue;
            if (builder.HasAntennaNames)
            {
                rfid.AddAntennaNames(context);
                rfid.AntennaNames!.Value = builder.AntennaNames;
            }
            if (builder.Provider is IAutoIdRfidProvider provider)
            {
                AutoIdRfidOperations operations = provider.SupportedOperations;
                if ((operations & AutoIdRfidOperations.ReadTag) != 0)
                {
                    rfid.AddReadTag(context);
                }
                if ((operations & AutoIdRfidOperations.WriteTag) != 0)
                {
                    rfid.AddWriteTag(context);
                }
                if ((operations & AutoIdRfidOperations.WriteTagId) != 0)
                {
                    rfid.AddWriteTagID(context);
                }
                if ((operations & AutoIdRfidOperations.KillTag) != 0)
                {
                    rfid.AddKillTag(context);
                }
                if ((operations & AutoIdRfidOperations.LockTag) != 0)
                {
                    rfid.AddLockTag(context);
                }
                if ((operations & AutoIdRfidOperations.SetTagPassword) != 0)
                {
                    rfid.AddSetTagPassword(context);
                }
            }
        }

        private static void ConfigureRtls(
            ISystemContext context,
            RtlsDeviceState rtls,
            AutoIdReaderBuilder builder,
            bool scan)
        {
            if (scan)
            {
                rtls.AddScan(context);
            }
            rtls.AddGetLocation(context).AddGetSupportedLocationTypes(context);
            AutoIdReaderBuilder.RtlsUnits units = builder.Units ?? DefaultUnits;
            rtls.LengthUnit!.Value = units.Length;
            rtls.RotationalUnit!.Value = units.Rotational;
            rtls.GeographicalUnit!.Value = units.Geographical;
            rtls.SpeedUnit!.Value = units.Speed;
        }

        /// <summary>
        /// The default RTLS units: metre, degree, degree and metre per second (UN/CEFACT common codes
        /// MTR, DD, DD and MTS).
        /// </summary>
        private static AutoIdReaderBuilder.RtlsUnits DefaultUnits =>
            new(
                Unit(5067858, "m", "metre"),
                Unit(17476, "deg", "degree"),
                Unit(17476, "deg", "degree"),
                Unit(5067859, "m/s", "metre per second"));

        private static EUInformation Unit(int unitId, string displayName, string description)
        {
            return new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = unitId,
                DisplayName = new LocalizedText(displayName),
                Description = new LocalizedText(description)
            };
        }
    }
}
