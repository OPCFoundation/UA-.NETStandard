#!/usr/bin/env python3
"""Add [DataType] and [DataTypeField] attributes to ApplicationConfiguration.cs"""

import re

file_path = r'Stack\Opc.Ua.Core\Schema\ApplicationConfiguration.cs'

with open(file_path, 'r', encoding='utf-8-sig') as f:
    lines = f.readlines()

content = ''.join(lines)
print(f"Read {len(lines)} lines, {len(content)} chars")


def find_line(lines, pattern, start=0):
    """Find line number (0-based) containing pattern."""
    for i in range(start, len(lines)):
        if pattern in lines[i]:
            return i
    return -1


def insert_before(lines, idx, text):
    """Insert text line before lines[idx], matching its indentation."""
    indent = ''
    m = re.match(r'^(\s*)', lines[idx])
    if m:
        indent = m.group(1)
    lines.insert(idx, indent + text + '\n')
    return 1  # number of lines inserted


def make_partial_at(lines, idx):
    """Make the class declaration at idx partial if not already."""
    if 'partial ' not in lines[idx]:
        lines[idx] = lines[idx].replace('public class ', 'public partial class ')
        lines[idx] = lines[idx].replace('public enum ', 'public partial enum ')  # won't apply normally
        return True
    return False


# Track offset as we insert lines
offset = 0

# ============================================================
# 1. ApplicationConfiguration (already partial)
# ============================================================
idx = find_line(lines, '[DataContract(Namespace = Namespaces.OpcUaConfig)]')
# Verify next line has ApplicationConfiguration
next_line = lines[idx + 1] if idx + 1 < len(lines) else ''
assert 'ApplicationConfiguration' in next_line, f"Expected ApplicationConfiguration, got: {next_line}"
offset += insert_before(lines, idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')

# Properties - find each DataMember within ApplicationConfiguration class
# We need to find properties between class start and class end

# ApplicationConfiguration properties
props = [
    ('ApplicationName', 0, None),
    ('ApplicationUri', 1, None),
    ('ProductUri', 2, None),
    ('ApplicationType', 3, None),
    ('SecurityConfiguration', 4, None),
    # SKIP TransportConfigurations (List<T>)
    ('TransportQuotas', 5, None),
    ('ServerConfiguration', 6, None),
    ('ClientConfiguration', 7, None),
    ('DiscoveryServerConfiguration', 8, None),
    ('Extensions', 9, None),
    ('TraceConfiguration', 10, None),
    ('DisableHiResClock', 11, None),
]

# Find the class body start
class_start = find_line(lines, 'public partial class ApplicationConfiguration')
for prop_name, order, name_attr in props:
    # Find [DataMember...] line followed by the property
    i = class_start
    while i < len(lines):
        if '[DataMember(' in lines[i]:
            # Check if next non-empty line has this property
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name}' in lines[j]:
                # Insert DataTypeField before DataMember
                if name_attr:
                    attr = f'[DataTypeField(Order = {order}, Name = "{name_attr}")]'
                else:
                    attr = f'[DataTypeField(Order = {order})]'
                offset += insert_before(lines, i, attr)
                break
        i += 1
    else:
        print(f"WARNING: Could not find {prop_name} in ApplicationConfiguration")

print(f"After ApplicationConfiguration: {len(lines)} lines")

# ============================================================
# 2. TransportQuotas - make partial
# ============================================================
idx = find_line(lines, '[DataContract(Namespace = Namespaces.OpcUaConfig)]',
                find_line(lines, 'public class TransportQuotas') - 2)
next_line = lines[idx + 1] if idx + 1 < len(lines) else ''
if 'TransportQuotas' in next_line:
    offset += insert_before(lines, idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')
    make_partial_at(lines, idx + 1 + 1)  # +1 for DataType, +1 for DataContract

# TransportQuotas properties
tq_props = [
    ('OperationTimeout', 0),
    ('MaxStringLength', 1),
    ('MaxByteStringLength', 2),
    ('MaxArrayLength', 3),
    ('MaxMessageSize', 4),
    ('MaxBufferSize', 5),
    ('MaxEncodingNestingLevels', 6),
    ('MaxDecoderRecoveries', 7),
    ('ChannelLifetime', 8),
    ('SecurityTokenLifetime', 9),
]

class_start = find_line(lines, 'public partial class TransportQuotas')
for prop_name, order in tq_props:
    i = class_start
    while i < len(lines):
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name} ' in lines[j]:
                attr = f'[DataTypeField(Order = {order})]'
                offset += insert_before(lines, i, attr)
                class_start = i + 2  # move past this property
                break
        i += 1

print(f"After TransportQuotas: {len(lines)} lines")

# ============================================================
# 3. TraceConfiguration - make partial
# ============================================================
idx = find_line(lines, 'public class TraceConfiguration') - 1
while '[DataContract(' not in lines[idx]:
    idx -= 1
offset += insert_before(lines, idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')
make_partial_at(lines, idx + 2)  # line after DataType and DataContract

tc_props = [
    ('OutputFilePath', 0),
    ('DeleteOnLoad', 1),
    ('TraceMasks', 2),
]

class_start = find_line(lines, 'public partial class TraceConfiguration')
for prop_name, order in tc_props:
    i = class_start
    while i < len(lines):
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name} ' in lines[j]:
                offset += insert_before(lines, i, f'[DataTypeField(Order = {order})]')
                class_start = i + 2
                break
        i += 1

print(f"After TraceConfiguration: {len(lines)} lines")

# ============================================================
# 4. TransportConfiguration - make partial
# ============================================================
idx = find_line(lines, 'public class TransportConfiguration') - 1
while '[DataContract(' not in lines[idx]:
    idx -= 1
offset += insert_before(lines, idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')
make_partial_at(lines, idx + 2)

tc2_props = [
    ('UriScheme', 0),
    ('TypeName', 1),
]

class_start = find_line(lines, 'public partial class TransportConfiguration')
for prop_name, order in tc2_props:
    i = class_start
    while i < len(lines):
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name} ' in lines[j]:
                offset += insert_before(lines, i, f'[DataTypeField(Order = {order})]')
                class_start = i + 2
                break
        i += 1

print(f"After TransportConfiguration: {len(lines)} lines")

# ============================================================
# 6. ServerSecurityPolicy - make partial
# ============================================================
idx = find_line(lines, 'public class ServerSecurityPolicy') - 1
while '[DataContract(' not in lines[idx]:
    idx -= 1
offset += insert_before(lines, idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')
make_partial_at(lines, idx + 2)

ssp_props = [
    ('SecurityMode', 0),
    ('SecurityPolicyUri', 1),
]

class_start = find_line(lines, 'public partial class ServerSecurityPolicy')
for prop_name, order in ssp_props:
    i = class_start
    while i < len(lines):
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name} ' in lines[j]:
                offset += insert_before(lines, i, f'[DataTypeField(Order = {order})]')
                class_start = i + 2
                break
        i += 1

print(f"After ServerSecurityPolicy: {len(lines)} lines")

# ============================================================
# 8. SecurityConfiguration - already partial
# ============================================================
idx = find_line(lines, 'public partial class SecurityConfiguration') - 1
while '[DataContract(' not in lines[idx]:
    idx -= 1
offset += insert_before(lines, idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')

# SecurityConfiguration properties - SKIP private ones (ApplicationCertificateLegacy, ApplicationCertificatesDataContract)
sc_props = [
    ('TrustedIssuerCertificates', 0),
    ('TrustedPeerCertificates', 1),
    ('NonceLength', 2),
    ('RejectedCertificateStore', 3),
    ('MaxRejectedCertificates', 4),
    ('AutoAcceptUntrustedCertificates', 5),
    ('UserRoleDirectory', 6),
    ('RejectSHA1SignedCertificates', 7),
    ('RejectUnknownRevocationStatus', 8),
    ('MinimumCertificateKeySize', 9),
    ('UseValidatedCertificates', 10),
    ('AddAppCertToTrustedStore', 11),
    ('SendCertificateChain', 12),
    ('UserIssuerCertificates', 13),
    ('TrustedUserCertificates', 14),
    ('HttpsIssuerCertificates', 15),
    ('TrustedHttpsCertificates', 16),
    ('SuppressNonceValidationErrors', 17),
]

class_start = find_line(lines, 'public partial class SecurityConfiguration')
for prop_name, order in sc_props:
    i = class_start
    found = False
    while i < len(lines):
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name}' in lines[j] and 'public ' in lines[j]:
                offset += insert_before(lines, i, f'[DataTypeField(Order = {order})]')
                class_start = i + 2
                found = True
                break
        i += 1
    if not found:
        print(f"WARNING: Could not find {prop_name} in SecurityConfiguration")

print(f"After SecurityConfiguration: {len(lines)} lines")

# ============================================================
# 9. SamplingRateGroup - make partial
# ============================================================
idx = find_line(lines, 'public class SamplingRateGroup') - 1
while '[DataContract(' not in lines[idx]:
    idx -= 1
offset += insert_before(lines, idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')
make_partial_at(lines, idx + 2)

srg_props = [
    ('Start', 0),
    ('Increment', 1),
    ('Count', 2),
]

class_start = find_line(lines, 'public partial class SamplingRateGroup')
for prop_name, order in srg_props:
    i = class_start
    while i < len(lines):
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name} ' in lines[j]:
                offset += insert_before(lines, i, f'[DataTypeField(Order = {order})]')
                class_start = i + 2
                break
        i += 1

print(f"After SamplingRateGroup: {len(lines)} lines")

# ============================================================
# 11. ServerBaseConfiguration - already partial
# ============================================================
idx = find_line(lines, 'public partial class ServerBaseConfiguration') - 1
while '[DataContract(' not in lines[idx]:
    idx -= 1
offset += insert_before(lines, idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')

sbc_props = [
    ('BaseAddresses', 0),
    ('AlternateBaseAddresses', 1),
    # SKIP SecurityPolicies (List<T>)
    ('MinRequestThreadCount', 2),
    ('MaxRequestThreadCount', 3),
    ('MaxQueuedRequestCount', 4),
]

class_start = find_line(lines, 'public partial class ServerBaseConfiguration')
# Find class end (next class declaration or end)
class_end = find_line(lines, 'public partial class ServerConfiguration')
for prop_name, order in sbc_props:
    i = class_start
    while i < class_end:
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name} ' in lines[j]:
                offset += insert_before(lines, i, f'[DataTypeField(Order = {order})]')
                class_start = i + 2
                class_end += 1  # we inserted a line
                break
        i += 1

print(f"After ServerBaseConfiguration: {len(lines)} lines")

# ============================================================
# 12. ServerConfiguration - already partial
# ============================================================
idx = find_line(lines, 'public partial class ServerConfiguration : ServerBaseConfiguration') - 1
while '[DataContract(' not in lines[idx]:
    idx -= 1
offset += insert_before(lines, idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')

sc2_props = [
    ('UserTokenPolicies', 0),
    ('DiagnosticsEnabled', 1),
    ('MaxSessionCount', 2),
    ('MaxChannelCount', 3),
    ('MinSessionTimeout', 4),
    ('MaxSessionTimeout', 5),
    ('MaxBrowseContinuationPoints', 6),
    ('MaxQueryContinuationPoints', 7),
    ('MaxHistoryContinuationPoints', 8),
    ('MaxRequestAge', 9),
    ('MinPublishingInterval', 10),
    ('MaxPublishingInterval', 11),
    ('PublishingResolution', 12),
    ('MaxSubscriptionLifetime', 13),
    ('MaxMessageQueueSize', 14),
    ('MaxNotificationQueueSize', 15),
    ('MaxNotificationsPerPublish', 16),
    ('MinMetadataSamplingInterval', 17),
    # SKIP AvailableSamplingRates (List<T>)
    ('RegistrationEndpoint', 18),
    ('MaxRegistrationInterval', 19),
    ('NodeManagerSaveFile', 20),
    ('MinSubscriptionLifetime', 21),
    ('MaxPublishRequestCount', 22),
    ('MaxSubscriptionCount', 23),
    ('MaxEventQueueSize', 24),
    ('ServerProfileArray', 25),
    ('ShutdownDelay', 26),
    ('ServerCapabilities', 27),
    ('SupportedPrivateKeyFormats', 28),
    ('MaxTrustListSize', 29),
    ('MultiCastDnsEnabled', 30),
    ('ReverseConnect', 31),
    ('OperationLimits', 32),
    ('AuditingEnabled', 33),
    ('HttpsMutualTls', 34),
    ('DurableSubscriptionsEnabled', 35),
    ('MaxDurableNotificationQueueSize', 36),
    ('MaxDurableEventQueueSize', 37),
    ('MaxDurableSubscriptionLifetimeInHours', 38),
]

class_start = find_line(lines, 'public partial class ServerConfiguration : ServerBaseConfiguration')
# Find end marker - next class
class_end = find_line(lines, 'public class ReverseConnectServerConfiguration', class_start)
if class_end == -1:
    class_end = find_line(lines, 'public partial class ReverseConnectServerConfiguration', class_start)

for prop_name, order in sc2_props:
    i = class_start
    found = False
    while i < class_end:
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name}' in lines[j] and ('public ' in lines[j] or 'public ' in lines[j-1] if j > 0 else False):
                # Additional check: make sure it's a property declaration (has { or ;)
                offset += insert_before(lines, i, f'[DataTypeField(Order = {order})]')
                class_start = i + 2
                class_end += 1
                found = True
                break
        i += 1
    if not found:
        print(f"WARNING: Could not find {prop_name} in ServerConfiguration")

print(f"After ServerConfiguration: {len(lines)} lines")

# ============================================================
# 13. ReverseConnectServerConfiguration - make partial
# ============================================================
idx = find_line(lines, 'public class ReverseConnectServerConfiguration')
if idx == -1:
    idx = find_line(lines, 'public partial class ReverseConnectServerConfiguration')
dc_idx = idx - 1
while '[DataContract(' not in lines[dc_idx]:
    dc_idx -= 1
offset += insert_before(lines, dc_idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')
# find the class line again after insert
idx = find_line(lines, 'public class ReverseConnectServerConfiguration')
if idx != -1:
    make_partial_at(lines, idx)

rcsc_props = [
    # SKIP Clients (List<T>)
    ('ConnectInterval', 0),
    ('ConnectTimeout', 1),
    ('RejectTimeout', 2),
]

class_start = find_line(lines, 'public partial class ReverseConnectServerConfiguration')
for prop_name, order in rcsc_props:
    i = class_start
    while i < len(lines):
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name} ' in lines[j]:
                offset += insert_before(lines, i, f'[DataTypeField(Order = {order})]')
                class_start = i + 2
                break
        i += 1

print(f"After ReverseConnectServerConfiguration: {len(lines)} lines")

# ============================================================
# 14. OperationLimits - make partial
# ============================================================
idx = find_line(lines, 'public class OperationLimits')
if idx == -1:
    idx = find_line(lines, 'public partial class OperationLimits')
dc_idx = idx - 1
while '[DataContract(' not in lines[dc_idx]:
    dc_idx -= 1
offset += insert_before(lines, dc_idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')
idx = find_line(lines, 'public class OperationLimits')
if idx != -1:
    make_partial_at(lines, idx)

ol_props = [
    ('MaxNodesPerRead', 0),
    ('MaxNodesPerHistoryReadData', 1),
    ('MaxNodesPerHistoryReadEvents', 2),
    ('MaxNodesPerWrite', 3),
    ('MaxNodesPerHistoryUpdateData', 4),
    ('MaxNodesPerHistoryUpdateEvents', 5),
    ('MaxNodesPerMethodCall', 6),
    ('MaxNodesPerBrowse', 7),
    ('MaxNodesPerRegisterNodes', 8),
    ('MaxNodesPerTranslateBrowsePathsToNodeIds', 9),
    ('MaxNodesPerNodeManagement', 10),
    ('MaxMonitoredItemsPerCall', 11),
]

class_start = find_line(lines, 'public partial class OperationLimits')
for prop_name, order in ol_props:
    i = class_start
    while i < len(lines):
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name} ' in lines[j]:
                offset += insert_before(lines, i, f'[DataTypeField(Order = {order})]')
                class_start = i + 2
                break
        i += 1

print(f"After OperationLimits: {len(lines)} lines")

# ============================================================
# 15. ReverseConnectClient - make partial
# ============================================================
idx = find_line(lines, 'public class ReverseConnectClient')
# Make sure it's not ReverseConnectClientCollection or ReverseConnectClientConfiguration or ReverseConnectClientEndpoint
# Find exact match
for i in range(len(lines)):
    stripped = lines[i].strip()
    if stripped == 'public class ReverseConnectClient' or stripped.startswith('public class ReverseConnectClient\n'):
        idx = i
        break
    elif 'public class ReverseConnectClient' in stripped and 'Collection' not in stripped and 'Configuration' not in stripped and 'Endpoint' not in stripped:
        idx = i
        break

dc_idx = idx - 1
while '[DataContract(' not in lines[dc_idx]:
    dc_idx -= 1
offset += insert_before(lines, dc_idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')
# Re-find after insert
for i in range(len(lines)):
    stripped = lines[i].strip()
    if 'public class ReverseConnectClient' in stripped and 'Collection' not in stripped and 'Configuration' not in stripped and 'Endpoint' not in stripped:
        make_partial_at(lines, i)
        break

rcc_props = [
    ('EndpointUrl', 0),
    ('Timeout', 1),
    ('MaxSessionCount', 2),
    ('Enabled', 3),
]

class_start = find_line(lines, 'public partial class ReverseConnectClient')
# Need exact match - not ReverseConnectClientConfiguration etc.
for i in range(len(lines)):
    stripped = lines[i].strip()
    if 'public partial class ReverseConnectClient' in stripped and 'Collection' not in stripped and 'Configuration' not in stripped and 'Endpoint' not in stripped:
        class_start = i
        break

# Find class end
class_end_marker = find_line(lines, 'ReverseConnectClientCollection', class_start + 1)
for prop_name, order in rcc_props:
    i = class_start
    while i < class_end_marker:
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name} ' in lines[j]:
                offset += insert_before(lines, i, f'[DataTypeField(Order = {order})]')
                class_start = i + 2
                class_end_marker += 1
                break
        i += 1

print(f"After ReverseConnectClient: {len(lines)} lines")

# ============================================================
# 17. ClientConfiguration - already partial
# ============================================================
idx = find_line(lines, 'public partial class ClientConfiguration') - 1
while '[DataContract(' not in lines[idx]:
    idx -= 1
offset += insert_before(lines, idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')

cc_props = [
    ('DefaultSessionTimeout', 0),
    ('WellKnownDiscoveryUrls', 1),
    ('DiscoveryServers', 2),
    ('EndpointCacheFilePath', 3),
    ('MinSubscriptionLifetime', 4),
    ('ReverseConnect', 5),
    ('OperationLimits', 6),
]

class_start = find_line(lines, 'public partial class ClientConfiguration')
class_end = find_line(lines, 'public class ReverseConnectClientConfiguration', class_start)
if class_end == -1:
    class_end = find_line(lines, 'public partial class ReverseConnectClientConfiguration', class_start)

for prop_name, order in cc_props:
    i = class_start
    found = False
    while i < class_end:
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name}' in lines[j] and 'public ' in lines[j]:
                offset += insert_before(lines, i, f'[DataTypeField(Order = {order})]')
                class_start = i + 2
                class_end += 1
                found = True
                break
        i += 1
    if not found:
        print(f"WARNING: Could not find {prop_name} in ClientConfiguration")

print(f"After ClientConfiguration: {len(lines)} lines")

# ============================================================
# 18. ReverseConnectClientConfiguration - make partial
# ============================================================
idx = find_line(lines, 'public class ReverseConnectClientConfiguration')
if idx == -1:
    idx = find_line(lines, 'public partial class ReverseConnectClientConfiguration')
dc_idx = idx - 1
while '[DataContract(' not in lines[dc_idx]:
    dc_idx -= 1
offset += insert_before(lines, dc_idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')
idx = find_line(lines, 'public class ReverseConnectClientConfiguration')
if idx != -1:
    make_partial_at(lines, idx)

rccc_props = [
    # SKIP ClientEndpoints (List<T>)
    ('HoldTime', 0),
    ('WaitTimeout', 1),
]

class_start = find_line(lines, 'public partial class ReverseConnectClientConfiguration')
for prop_name, order in rccc_props:
    i = class_start
    while i < len(lines):
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name} ' in lines[j]:
                offset += insert_before(lines, i, f'[DataTypeField(Order = {order})]')
                class_start = i + 2
                break
        i += 1

print(f"After ReverseConnectClientConfiguration: {len(lines)} lines")

# ============================================================
# 19. ReverseConnectClientEndpoint - make partial
# ============================================================
idx = find_line(lines, 'public class ReverseConnectClientEndpoint')
# Not collection
for i in range(len(lines)):
    if 'public class ReverseConnectClientEndpoint' in lines[i] and 'Collection' not in lines[i]:
        idx = i
        break
dc_idx = idx - 1
while '[DataContract(' not in lines[dc_idx]:
    dc_idx -= 1
offset += insert_before(lines, dc_idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')
for i in range(len(lines)):
    if 'public class ReverseConnectClientEndpoint' in lines[i] and 'Collection' not in lines[i]:
        make_partial_at(lines, i)
        break

rcce_props = [
    ('EndpointUrl', 0),
]

# Find class start - exact match
for i in range(len(lines)):
    if 'public partial class ReverseConnectClientEndpoint' in lines[i] and 'Collection' not in lines[i]:
        class_start = i
        break

for prop_name, order in rcce_props:
    i = class_start
    while i < len(lines):
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name} ' in lines[j]:
                offset += insert_before(lines, i, f'[DataTypeField(Order = {order})]')
                class_start = i + 2
                break
        i += 1

print(f"After ReverseConnectClientEndpoint: {len(lines)} lines")

# ============================================================
# 21. DiscoveryServerConfiguration - make partial
# ============================================================
idx = find_line(lines, 'public class DiscoveryServerConfiguration')
if idx == -1:
    idx = find_line(lines, 'public partial class DiscoveryServerConfiguration')
dc_idx = idx - 1
while '[DataContract(' not in lines[dc_idx]:
    dc_idx -= 1
offset += insert_before(lines, dc_idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')
idx = find_line(lines, 'public class DiscoveryServerConfiguration')
if idx != -1:
    make_partial_at(lines, idx)

dsc_props = [
    ('ServerNames', 0),
    ('DiscoveryServerCacheFile', 1),
    # SKIP ServerRegistrations (List<T>)
]

class_start = find_line(lines, 'public partial class DiscoveryServerConfiguration')
for prop_name, order in dsc_props:
    i = class_start
    while i < len(lines):
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name} ' in lines[j]:
                offset += insert_before(lines, i, f'[DataTypeField(Order = {order})]')
                class_start = i + 2
                break
        i += 1

print(f"After DiscoveryServerConfiguration: {len(lines)} lines")

# ============================================================
# 22. ServerRegistration - make partial
# ============================================================
idx = find_line(lines, 'public class ServerRegistration')
# Not collection
for i in range(len(lines)):
    if 'public class ServerRegistration' in lines[i] and 'Collection' not in lines[i]:
        idx = i
        break
dc_idx = idx - 1
while '[DataContract(' not in lines[dc_idx]:
    dc_idx -= 1
offset += insert_before(lines, dc_idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')
for i in range(len(lines)):
    if 'public class ServerRegistration' in lines[i] and 'Collection' not in lines[i]:
        make_partial_at(lines, i)
        break

sr_props = [
    ('ApplicationUri', 0),
    ('AlternateDiscoveryUrls', 1),
]

for i in range(len(lines)):
    if 'public partial class ServerRegistration' in lines[i] and 'Collection' not in lines[i]:
        class_start = i
        break

for prop_name, order in sr_props:
    i = class_start
    while i < len(lines):
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name} ' in lines[j]:
                offset += insert_before(lines, i, f'[DataTypeField(Order = {order})]')
                class_start = i + 2
                break
        i += 1

print(f"After ServerRegistration: {len(lines)} lines")

# ============================================================
# 24. CertificateStoreIdentifier - already partial
# ============================================================
idx = find_line(lines, 'public partial class CertificateStoreIdentifier') - 1
while '[DataContract(' not in lines[idx]:
    idx -= 1
offset += insert_before(lines, idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')

csi_props = [
    ('StoreType', 0),
    ('StorePath', 1),
    # SKIP XmlEncodedValidationOptions (internal)
]

class_start = find_line(lines, 'public partial class CertificateStoreIdentifier')
# Find class end
class_end = find_line(lines, 'CertificateTrustList', class_start + 1)
for prop_name, order in csi_props:
    i = class_start
    while i < class_end:
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name}' in lines[j] and 'public ' in lines[j]:
                offset += insert_before(lines, i, f'[DataTypeField(Order = {order})]')
                class_start = i + 2
                class_end += 1
                break
        i += 1

print(f"After CertificateStoreIdentifier: {len(lines)} lines")

# ============================================================
# 25. CertificateTrustList - already partial, no DataTypeField properties
# ============================================================
idx = find_line(lines, 'public partial class CertificateTrustList') - 1
# Go up to find [DataContract line - but there might be [KnownType] in between
while '[DataContract(' not in lines[idx]:
    idx -= 1
offset += insert_before(lines, idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')

print(f"After CertificateTrustList: {len(lines)} lines")

# ============================================================
# 27. CertificateIdentifier - already partial
# ============================================================
idx = find_line(lines, 'public partial class CertificateIdentifier') - 1
# Careful: CertificateIdentifierCollection is also partial
# Find the exact line
for i in range(len(lines)):
    if 'public partial class CertificateIdentifier' in lines[i] and 'Collection' not in lines[i]:
        target_idx = i
        break

dc_idx = target_idx - 1
while '[DataContract(' not in lines[dc_idx]:
    dc_idx -= 1
offset += insert_before(lines, dc_idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')

ci_props = [
    ('StoreType', 0),
    ('StorePath', 1),
    ('SubjectName', 2),
    ('Thumbprint', 3),
    ('RawData', 4),
    # SKIP XmlEncodedValidationOptions (internal)
    ('CertificateType', 5),
    ('CertificateTypeString', 6),
]

# Find class start - exact
for i in range(len(lines)):
    if 'public partial class CertificateIdentifier' in lines[i] and 'Collection' not in lines[i]:
        class_start = i
        break

# Find class end
class_end = find_line(lines, 'public partial class ConfiguredEndpointCollection', class_start)

for prop_name, order in ci_props:
    i = class_start
    found = False
    while i < class_end:
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name}' in lines[j] and 'public ' in lines[j]:
                offset += insert_before(lines, i, f'[DataTypeField(Order = {order})]')
                class_start = i + 2
                class_end += 1
                found = True
                break
        i += 1
    if not found:
        print(f"WARNING: Could not find {prop_name} in CertificateIdentifier")

print(f"After CertificateIdentifier: {len(lines)} lines")

# ============================================================
# 28. ConfiguredEndpointCollection - already partial
# ============================================================
idx = find_line(lines, 'public partial class ConfiguredEndpointCollection') - 1
while '[DataContract(' not in lines[idx]:
    idx -= 1
offset += insert_before(lines, idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')

cec_props = [
    ('KnownHosts', 0, None),
    # SKIP Endpoints (List<ConfiguredEndpoint>)
    ('TcpProxyUrl', 1, None),
]

class_start = find_line(lines, 'public partial class ConfiguredEndpointCollection')
class_end = find_line(lines, 'public partial class ConfiguredEndpoint', class_start + 1)

for prop_name, order, name_attr in cec_props:
    i = class_start
    while i < class_end:
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name}' in lines[j] and 'public ' in lines[j]:
                if name_attr:
                    attr = f'[DataTypeField(Order = {order}, Name = "{name_attr}")]'
                else:
                    attr = f'[DataTypeField(Order = {order})]'
                offset += insert_before(lines, i, attr)
                class_start = i + 2
                class_end += 1
                break
        i += 1

print(f"After ConfiguredEndpointCollection: {len(lines)} lines")

# ============================================================
# 29. ConfiguredEndpoint - already partial
# ============================================================
# Find [DataContract for ConfiguredEndpoint (not Collection)
for i in range(len(lines)):
    if 'public partial class ConfiguredEndpoint' in lines[i] and 'Collection' not in lines[i]:
        target_idx = i
        break

dc_idx = target_idx - 1
while '[DataContract(' not in lines[dc_idx]:
    dc_idx -= 1
offset += insert_before(lines, dc_idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')

ce_props = [
    ('Description', 0, 'Endpoint'),
    ('Configuration', 1, 'Configuration'),
    ('UpdateBeforeConnect', 2, 'UpdateBeforeConnect'),
    ('BinaryEncodingSupport', 3, 'BinaryEncodingSupport'),
    ('SelectedUserTokenPolicyIndex', 4, 'SelectedUserTokenPolicy'),
    ('UserIdentity', 5, 'UserIdentity'),
    ('ReverseConnect', 6, 'ReverseConnect'),
    ('Extensions', 7, None),
]

# Find class start
for i in range(len(lines)):
    if 'public partial class ConfiguredEndpoint' in lines[i] and 'Collection' not in lines[i]:
        class_start = i
        break

class_end = find_line(lines, 'public enum BinaryEncodingSupport', class_start)
if class_end == -1:
    class_end = find_line(lines, 'public partial enum BinaryEncodingSupport', class_start)

for prop_name, order, name_attr in ce_props:
    i = class_start
    found = False
    while i < class_end:
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name}' in lines[j] and 'public ' in lines[j]:
                if name_attr:
                    attr = f'[DataTypeField(Order = {order}, Name = "{name_attr}")]'
                else:
                    attr = f'[DataTypeField(Order = {order})]'
                offset += insert_before(lines, i, attr)
                class_start = i + 2
                class_end += 1
                found = True
                break
        i += 1
    if not found:
        print(f"WARNING: Could not find {prop_name} in ConfiguredEndpoint")

print(f"After ConfiguredEndpoint: {len(lines)} lines")

# ============================================================
# 30. BinaryEncodingSupport enum
# ============================================================
idx = find_line(lines, 'public enum BinaryEncodingSupport')
dc_idx = idx - 1
while '[DataContract(' not in lines[dc_idx]:
    dc_idx -= 1
offset += insert_before(lines, dc_idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')

print(f"After BinaryEncodingSupport: {len(lines)} lines")

# ============================================================
# 31. ReverseConnectEndpoint - make partial
# ============================================================
idx = find_line(lines, 'public class ReverseConnectEndpoint')
if idx == -1:
    idx = find_line(lines, 'public partial class ReverseConnectEndpoint')
dc_idx = idx - 1
while '[DataContract(' not in lines[dc_idx]:
    dc_idx -= 1
offset += insert_before(lines, dc_idx, '[DataType(Namespace = Namespaces.OpcUaConfig)]')
idx = find_line(lines, 'public class ReverseConnectEndpoint')
if idx != -1:
    make_partial_at(lines, idx)

rce_props = [
    ('Enabled', 0, 'Enabled'),
    ('ServerUri', 1, 'ServerUri'),
    ('Thumbprint', 2, 'Thumbprint'),
]

class_start = find_line(lines, 'public partial class ReverseConnectEndpoint')
for prop_name, order, name_attr in rce_props:
    i = class_start
    while i < len(lines):
        if '[DataMember(' in lines[i]:
            j = i + 1
            while j < len(lines) and lines[j].strip() == '':
                j += 1
            if j < len(lines) and f' {prop_name} ' in lines[j]:
                attr = f'[DataTypeField(Order = {order}, Name = "{name_attr}")]'
                offset += insert_before(lines, i, attr)
                class_start = i + 2
                break
        i += 1

print(f"After ReverseConnectEndpoint: {len(lines)} lines")

# ============================================================
# Write the result
# ============================================================
with open(file_path, 'w', encoding='utf-8') as f:
    f.writelines(lines)

print(f"\nDone! Final line count: {len(lines)}")
