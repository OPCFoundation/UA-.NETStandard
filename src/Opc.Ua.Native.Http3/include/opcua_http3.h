/* Copyright (c) OPC Foundation. Licensed under the MIT License.
 * Production Sans-I/O ABI 1 / operation revision 2. No QUIC/TLS or callbacks.
 */
#ifndef OPCUA_HTTP3_H
#define OPCUA_HTTP3_H
#include <stddef.h>
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif

typedef struct opcua_http3_peer opcua_http3_peer;

typedef struct opcua_http3_utf8 {
    const uint8_t *data;
    size_t length;
} opcua_http3_utf8;

/* 80 bytes. Assertions supplied ONLY by the real authenticated transport backend. */
typedef struct opcua_http3_transport_facts {
    uint32_t abi_version; /* 1 */
    uint32_t role; /* 0 client, 1 server */
    uint64_t local_max_datagram;
    uint64_t peer_max_datagram;
    uint64_t reliable_reset_parameter; /* 0x1d */
    uint64_t reliable_reset_frame; /* 0x24 */
    uint64_t initial_max_bidi; /* local WT credit: 1..128 */
    uint64_t initial_max_data; /* local WT credit: 1..16MiB */
    uint32_t local_reset_parameter_empty; /* 1 */
    uint32_t peer_reset_parameter_empty; /* 1 */
    uint32_t handshake_confirmed; /* 1, never 0-RTT */
    uint32_t mutual_tls_authenticated; /* 1, host validated expected applications */
    uint32_t alpn_is_h3; /* 1 */
    uint32_t reserved; /* 0 */
} opcua_http3_transport_facts;

typedef struct opcua_http3_limits {
    uint64_t input_bytes;       /* 16..1048576 */
    uint64_t output_bytes;      /* 4096..1048576, queued + leased */
    uint64_t pending_sends;     /* 8..512 */
    uint64_t output_events;     /* 64..4096; >= header_bytes/32 + 8 */
    uint64_t lifetime_streams;  /* 7..4096; includes control/CONNECT/tombstones */
    uint64_t header_count;      /* 8..128 */
    uint64_t header_bytes;      /* 1024..16384, includes field overhead */
    uint64_t control_bytes;     /* 128..65536 per peer uni stream over its lifetime */
    uint64_t request_bytes;     /* 1024..65536, parser capacity + ingress */
    uint64_t capsule_bytes;     /* 1024..65536, decoder capacity + ingress */
    uint64_t pending_bytes;     /* 1024..131072 before session establishment */
    uint64_t receipts;          /* 1..256 */
} opcua_http3_limits;

/* 224 bytes on accepted x64 RIDs. No zero/default endpoint or trust policy. */
typedef struct opcua_http3_config {
    uint32_t struct_size; /* sizeof(opcua_http3_config) */
    uint32_t abi_version; /* 1 */
    uint32_t origin_policy; /* 0 absent-only; 1 absent or exact; 2 required exact */
    uint32_t maximum_sessions; /* exactly 1 for connection lifetime */
    opcua_http3_utf8 authority; /* canonical ASCII host[:port] or [IPv6][:port] */
    opcua_http3_utf8 path; /* exact absolute URI resource, optional query */
    opcua_http3_utf8 origin; /* empty for mode0; otherwise exact https://authority */
    opcua_http3_utf8 application_protocol; /* nonempty identifier */
    uint64_t control_stream; /* already allocated, distinct local QUIC uni IDs */
    uint64_t encoder_stream;
    uint64_t decoder_stream;
    uint64_t connect_stream; /* reserved client bidi0, even on server */
    uint64_t connection_tag; /* nonzero external correlation; NOT authentication */
    uint64_t application_pair_tag; /* nonzero external correlation; NOT authentication */
    opcua_http3_limits limits;
} opcua_http3_config;

/* 48 bytes. Send payload is copied into the caller buffer; arg is its lease token. */
typedef struct opcua_http3_event {
    uint32_t kind;
    uint32_t flags;
    uint64_t stream;
    uint64_t session; /* UINT64_MAX means unassociated; 0 is a valid session */
    uint64_t arg;
    uint64_t aux;
    uint64_t data_len;
} opcua_http3_event;

enum opcua_http3_scope {
    OPCUA_HTTP3_SCOPE_NONE = 0, OPCUA_HTTP3_SCOPE_OPERATION = 1,
    OPCUA_HTTP3_SCOPE_STREAM = 2, OPCUA_HTTP3_SCOPE_SESSION = 3,
    OPCUA_HTTP3_SCOPE_CONNECTION = 4, OPCUA_HTTP3_SCOPE_CONFIGURATION = 5
};

enum opcua_http3_code {
    OPCUA_HTTP3_OK = 0, OPCUA_HTTP3_INVALID_ABI = 1, OPCUA_HTTP3_INVALID_ARGUMENT = 2,
    OPCUA_HTTP3_INVALID_AUTHORITY = 3, OPCUA_HTTP3_INVALID_PATH = 4,
    OPCUA_HTTP3_INVALID_ORIGIN = 5, OPCUA_HTTP3_INVALID_PROTOCOL = 6,
    OPCUA_HTTP3_INVALID_LIMITS = 7, OPCUA_HTTP3_INVALID_TRANSPORT_EVIDENCE = 8,
    OPCUA_HTTP3_INVALID_STREAM_ID = 9, OPCUA_HTTP3_INVALID_OPERATION = 10,
    OPCUA_HTTP3_WRONG_ROLE = 11, OPCUA_HTTP3_INVALID_STATE = 12,
    OPCUA_HTTP3_UNKNOWN_STREAM = 13, OPCUA_HTTP3_UNKNOWN_SESSION = 14,
    OPCUA_HTTP3_LIMIT_EXCEEDED = 15, OPCUA_HTTP3_BACKPRESSURE = 16,
    OPCUA_HTTP3_FLOW_CONTROL_BLOCKED = 17, OPCUA_HTTP3_INVALID_COMPLETION = 18,
    OPCUA_HTTP3_INVALID_RESET_EVIDENCE = 19, OPCUA_HTTP3_SEND_FAILED = 20,
    OPCUA_HTTP3_PEER_PROTOCOL = 21, OPCUA_HTTP3_SETTINGS_MISMATCH = 22,
    OPCUA_HTTP3_AUTHORITY_DENIED = 23, OPCUA_HTTP3_PATH_DENIED = 24,
    OPCUA_HTTP3_ORIGIN_DENIED = 25, OPCUA_HTTP3_APPLICATION_PROTOCOL_DENIED = 26,
    OPCUA_HTTP3_UNSUPPORTED_DRAFT = 27, OPCUA_HTTP3_SESSION_LIMIT = 28,
    OPCUA_HTTP3_PANIC = 29
};

/* 48 bytes. Wire error code 0 means local error. No secrets in diagnostics. */
typedef struct opcua_http3_error {
    uint32_t scope;
    uint32_t code;
    uint64_t protocol_code;
    uint64_t stream;
    uint64_t session;
    uint64_t message_len;
    uint32_t operation;
    uint32_t flags; /* 1 retryable; 2 connection fatal */
} opcua_http3_error;

typedef struct opcua_http3_resource_snapshot_data {
    uint64_t capsule_bytes;
    uint64_t request_bytes;
    uint64_t queued_bytes;
    uint64_t leased_bytes;
    uint64_t pending_sends;
    uint64_t reserved_payload;
    uint64_t maximum_output_bytes;
    uint64_t revision;
} opcua_http3_resource_snapshot_data;

typedef struct opcua_http3_layout_data {
    uint32_t struct_size, abi_version, api_revision, pointer_size;
    uint32_t facts_size, config_size, event_size, error_size, resource_size, reserved;
} opcua_http3_layout_data;

uint32_t opcua_http3_abi_version(void);
uint32_t opcua_http3_api_revision(void);
int32_t opcua_http3_layout(opcua_http3_layout_data *out, size_t size);
int32_t opcua_http3_create(const opcua_http3_transport_facts *facts,
    const opcua_http3_config *config, opcua_http3_peer **out, opcua_http3_error *error);
int32_t opcua_http3_command(opcua_http3_peer *handle, uint32_t operation,
    uint64_t id, uint64_t arg, uint64_t aux, const uint8_t *data, size_t length);
int32_t opcua_http3_poll(opcua_http3_peer *handle, opcua_http3_event *event,
    uint8_t *buffer, size_t capacity);
int32_t opcua_http3_last_error(const opcua_http3_peer *handle, opcua_http3_error *error,
    uint8_t *buffer, size_t capacity);
int32_t opcua_http3_resource_snapshot(const opcua_http3_peer *handle,
    opcua_http3_resource_snapshot_data *out);
void opcua_http3_destroy(opcua_http3_peer *handle);

/* See ABI.md for operation/event numbers and exact retry/ownership semantics.
 * op20: id=token, arg=cumulative accepted bytes; aux=0 progress,1 released,
 *       3 released+FIN accepted,5 released+terminal failure.
 * op22: id=stream, arg=actual LOCAL reset final size, aux=actual reliable size.
 * Successful poll1/2 is a lease, never transport success. Prefix flag2 requires
 * setting the transport reliable boundary before acknowledgement/body admission.
 */
#ifdef __cplusplus
}
#endif
#endif
