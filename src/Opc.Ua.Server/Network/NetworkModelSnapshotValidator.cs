// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;

namespace Opc.Ua.Server.Network
{
    /// <summary>
    /// Contains snapshot validation shared by provider implementations and the server publication boundary.
    /// </summary>
    internal static class NetworkModelSnapshotValidator
    {
        /// <summary>
        /// Determines whether a TSN StreamId contains the eight bytes required by Part 22.
        /// </summary>
        /// <param name="streamId">The stream identity to validate.</param>
        /// <returns>True when the identity contains exactly eight bytes.</returns>
        internal static bool IsValidStreamId(ByteString streamId)
        {
            return streamId.Length == 8;
        }

        /// <summary>
        /// Determines whether an optional TSN stream reservation class identifier is in the defined range.
        /// </summary>
        /// <param name="srClassId">The optional stream reservation class identifier.</param>
        /// <returns>True when the identifier is absent or between zero and seven.</returns>
        internal static bool IsValidSrClassId(byte? srClassId)
        {
            return !srClassId.HasValue || srClassId.Value <= 7;
        }

        /// <summary>
        /// Determines whether an optional network-interface VLAN identifier is in the accepted range.
        /// </summary>
        /// <param name="vlanId">The optional VLAN identifier.</param>
        /// <returns>True when the identifier is absent or between zero and 4094.</returns>
        internal static bool IsValidInterfaceVlanId(ushort? vlanId)
        {
            return !vlanId.HasValue || vlanId.Value <= 4094;
        }

        /// <summary>
        /// Determines whether a stream-reservation class VLAN identifier is in the accepted range.
        /// </summary>
        /// <param name="vid">The VLAN identifier.</param>
        /// <returns>True when the identifier is between zero and 4094.</returns>
        internal static bool IsValidSrClassVid(ushort vid)
        {
            return vid <= 4094;
        }

        /// <summary>
        /// Determines whether the optional stream MAC addresses have valid lengths and dependencies.
        /// </summary>
        /// <param name="sourceAddress">The optional stream source address.</param>
        /// <param name="destinationAddress">The optional stream destination address.</param>
        /// <returns>True when both values are absent or valid and a source has a destination.</returns>
        internal static bool AreValidStreamMacAddresses(ByteString sourceAddress, ByteString destinationAddress)
        {
            return (sourceAddress.Length is 0 or 6) &&
                (destinationAddress.Length is 0 or 6) &&
                (sourceAddress.Length == 0 || destinationAddress.Length == 6);
        }

        /// <summary>
        /// Determines whether each failure-system identifier contains the eight bytes required by Part 22.
        /// </summary>
        /// <param name="identifiers">The failure-system identifiers to validate.</param>
        /// <returns>True when every identifier contains exactly eight bytes.</returns>
        internal static bool AreValidFailureSystemIdentifiers(ArrayOf<ByteString> identifiers)
        {
            foreach (ByteString identifier in identifiers)
            {
                if (identifier.Length != 8)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Determines whether an optional traffic specification has a non-zero interval denominator.
        /// </summary>
        /// <param name="traffic">The traffic specification to validate.</param>
        /// <returns>True when the interval denominator is non-zero.</returns>
        internal static bool IsValidTraffic(NetworkTrafficSnapshot traffic)
        {
            return traffic.Denominator != 0;
        }

        /// <summary>
        /// Determines whether an acquisition timestamp is explicitly UTC.
        /// </summary>
        /// <param name="timestamp">The acquisition timestamp.</param>
        /// <returns>True when the timestamp kind is UTC.</returns>
        internal static bool IsUtcTimestamp(DateTime timestamp)
        {
            return timestamp.Kind == DateTimeKind.Utc;
        }

        /// <summary>
        /// Determines whether every SR class has a unique identifier within one interface.
        /// </summary>
        /// <param name="srClasses">The interface's SR classes.</param>
        /// <returns>True when all SR-class identifiers are unique.</returns>
        internal static bool AreUniqueSrClassIds(ArrayOf<NetworkSrClassSnapshot> srClasses)
        {
            var ids = new HashSet<byte>();
            foreach (NetworkSrClassSnapshot srClass in srClasses)
            {
                if (srClass is null || !ids.Add(srClass.Id))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Determines whether every stream interface has a non-empty, unique identifier within one stream.
        /// </summary>
        /// <param name="interfaces">The stream's interface configurations.</param>
        /// <returns>True when all identifiers are non-empty and unique.</returns>
        internal static bool AreUniqueStreamInterfaceIds(ArrayOf<NetworkStreamInterfaceSnapshot> interfaces)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (NetworkStreamInterfaceSnapshot streamInterface in interfaces)
            {
                if (streamInterface is null ||
                    string.IsNullOrWhiteSpace(streamInterface.Id) ||
                    !ids.Add(streamInterface.Id))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Determines whether an LLDP destination MAC address has its required six bytes.
        /// </summary>
        /// <param name="address">The destination MAC address.</param>
        /// <returns>True when the address contains exactly six bytes.</returns>
        internal static bool IsValidLldpDestinationMacAddress(ByteString address)
        {
            return address.Length == 6;
        }

        /// <summary>
        /// Validates local management-address structures supplied for LLDP transmission.
        /// </summary>
        /// <param name="addresses">The local port management addresses.</param>
        /// <returns>True when each address has its mandatory string and a defined interface subtype.</returns>
        internal static bool AreValidLldpManagementAddressTxPorts(
            ArrayOf<LldpManagementAddressTxPortType> addresses)
        {
            foreach (LldpManagementAddressTxPortType address in addresses)
            {
                if (address is null ||
                    address.ManAddress is null ||
                    !IsDefinedEnum(address.IfSubtype))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Validates remote management-address structures received through LLDP.
        /// </summary>
        /// <param name="addresses">The remote system management addresses.</param>
        /// <returns>True when each address has its mandatory string and a defined interface subtype.</returns>
        internal static bool AreValidLldpManagementAddresses(ArrayOf<LldpManagementAddressType> addresses)
        {
            foreach (LldpManagementAddressType address in addresses)
            {
                if (address is null ||
                    address.Address is null ||
                    !IsDefinedEnum(address.IfSubtype))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Validates unrecognized LLDP TLV structures.
        /// </summary>
        /// <param name="tlvs">The unrecognized TLVs.</param>
        /// <returns>True when every structure is present.</returns>
        internal static bool AreValidLldpTlvs(ArrayOf<LldpTlvType> tlvs)
        {
            foreach (LldpTlvType tlv in tlvs)
            {
                if (tlv is null)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Determines whether an enum value is defined by its generated OPC UA enumeration.
        /// </summary>
        /// <typeparam name="TEnum">The generated enumeration type.</typeparam>
        /// <param name="value">The value to validate.</param>
        /// <returns>True when the value is defined.</returns>
        internal static bool IsDefinedEnum<TEnum>(TEnum value)
            where TEnum : struct, Enum
        {
#if NET8_0_OR_GREATER
            return Enum.IsDefined(value);
#else
            return Enum.IsDefined(typeof(TEnum), value);
#endif
        }

        /// <summary>
        /// Validates priority-mapping entry keys and optional PCP/DSCP values.
        /// </summary>
        /// <param name="entry">The priority-mapping entry to validate.</param>
        /// <returns>Good for valid entries and BadInvalidArgument otherwise.</returns>
        internal static StatusCode ValidatePriorityMappingEntry(PriorityMappingEntryType entry)
        {
            if (string.IsNullOrWhiteSpace(entry.MappingUri) ||
                string.IsNullOrWhiteSpace(entry.PriorityLabel))
            {
                return StatusCodes.BadInvalidArgument;
            }
            if (entry.PriorityValue_PCP != byte.MaxValue && entry.PriorityValue_PCP > 7)
            {
                return StatusCodes.BadInvalidArgument;
            }
            if (entry.PriorityValue_DSCP != uint.MaxValue && entry.PriorityValue_DSCP > 63)
            {
                return StatusCodes.BadInvalidArgument;
            }
            return StatusCodes.Good;
        }

        /// <summary>
        /// Validates mapping entries and their composite keys within one table.
        /// </summary>
        /// <param name="entries">The table's priority-mapping entries.</param>
        /// <returns>True when every entry is valid and its key is unique in the table.</returns>
        internal static bool AreValidPriorityMappingEntries(ArrayOf<PriorityMappingEntryType> entries)
        {
            var keys = new HashSet<(string? MappingUri, string? PriorityLabel)>();
            foreach (PriorityMappingEntryType entry in entries)
            {
                if (entry is null ||
                    StatusCode.IsBad(ValidatePriorityMappingEntry(entry)) ||
                    !keys.Add((entry.MappingUri, entry.PriorityLabel)))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
