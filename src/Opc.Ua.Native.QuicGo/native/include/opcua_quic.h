/* Copyright (c) OPC Foundation. Licensed under the MIT License. */
#ifndef OPCUA_QUIC_H
#define OPCUA_QUIC_H
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif

/* ABI 7. x64, natural alignment (8), Cdecl. Never pass a Go pointer to C.
 * Handles are monotonic, type-checked, and never recycled in this process.
 * The library MUST remain loaded for the lifetime of the process. */
typedef struct { uint64_t value; } uqg_listener;
typedef struct { uint64_t value; } uqg_connection;
typedef struct { uint64_t value; } uqg_stream;
typedef struct { uint64_t value; } uqg_operation;
typedef struct {
    uint8_t *data;
    uint32_t length;
    uint32_t reserved;
} uqg_bytes;

enum uqg_status {
    UQG_OK = 0, UQG_PENDING = 1, UQG_INVALID = -1, UQG_AUTHENTICATION = -3,
    UQG_TOO_LARGE = -4, UQG_CLOSED = -5, UQG_LIMIT = -6,
    UQG_BUFFER_SMALL = -7, UQG_TIMEOUT = -8, UQG_INTERNAL = -9,
    UQG_CANCELLED = -10, UQG_BUSY = -11, UQG_CAPABILITY = -12
};
enum {
    UQG_MAX_OPERATIONS = 128, UQG_MAX_RETAINED_BYTES = 4194304,
    UQG_MAX_LISTENERS = 8, UQG_MAX_CONNECTIONS = 32, UQG_MAX_STREAMS = 256,
    UQG_MAX_PAYLOAD = 65536, UQG_MAX_PEERS = 32
};
enum uqg_kind {
    UQG_LISTEN = 1, UQG_CONNECT = 2,
    UQG_LISTENER_ACCEPT = 3, UQG_LISTENER_ADDRESS = 4, UQG_LISTENER_CLOSE = 5,
    UQG_CONNECTION_INFO = 10, UQG_CONNECTION_CERTIFICATE = 11,
    UQG_STREAM_OPEN = 12, UQG_STREAM_ACCEPT = 13,
    UQG_DATAGRAM_SEND = 14, UQG_DATAGRAM_RECEIVE = 15,
    UQG_CONNECTION_CLOSE = 16, UQG_CONNECTION_WAIT_CLOSED = 17,
    UQG_CONNECTION_RELEASE = 18,
    UQG_CONNECTION_EXPORTER = 19,
    UQG_CONNECTION_EXPORTER_CUSTOM = 30,
    UQG_STREAM_SEND = 20, UQG_STREAM_READ = 21,
    UQG_STREAM_RESET = 22, UQG_STREAM_RESET_SENT = 23,
    UQG_STREAM_STOP = 24, UQG_STREAM_WAIT_STOPPED = 25,
    UQG_STREAM_SENT_OFFSET = 26, UQG_STREAM_RELEASE = 27,
    UQG_STREAM_RESET_RECEIVED = 28, UQG_STREAM_PEER_STOPPED = 29,
    UQG_STREAM_RELEASE_GRACEFUL = 31
};
enum {
    UQG_UNIDIRECTIONAL = 1, UQG_SEND_FIN = 1, UQG_SEND_RELIABLE_BOUNDARY = 2,
    UQG_READ_DATA = 0, UQG_READ_FIN = 1, UQG_READ_RESET = 2
};
enum uqg_authentication_policy {
    UQG_APPLICATION_MTLS = 0,
    UQG_SERVER_AUTHENTICATED_TLS = 1
};

/* Each DER/key bundle <=16384 bytes, <=16 certificates. No files, system trust,
 * generated identity, PEM interpretation, hardware-key fallback or key callback.
 * PKCS8: unencrypted two-prime RSA2048..4096 or ECDSA P256/P384/P521.
 * Application-mTLS policy requires local_uri to be an exact URI SAN.
 * Server-authenticated policy requires empty local_uri and no peer allowlist:
 * connector supplies only trust_roots_der and server_name; listener supplies
 * certificate_der, optional issuer_chain_der and private_key_pkcs8.
 * server_name is required for connectors. No client certificate selector is installed.
 * An exporter unable to supply a required key MUST refuse the operation.
 * CONNECTION_EXPORTER accepts exactly a 32-byte context and returns the 32-byte
 * TLS exporter output for EXPORTER-OPCUA-QUIC-DRAFT.
 * CONNECTION_EXPORTER_CUSTOM accepts [labelLength:8][label][contextLength:8]
 * [context][outputLength:8] and returns the requested TLS exporter output. */
typedef struct {
    uqg_bytes certificate_der;
    uqg_bytes issuer_chain_der;
    uqg_bytes private_key_pkcs8;
    uqg_bytes trust_roots_der;
    uqg_bytes local_uri;
    uqg_bytes server_name;
} uqg_identity;

/* One or more explicit application identities, not HELLO assertions.
 * Every rule requires BOTH an exact URI SAN and a 32-byte SHA256 SPKI pin.
 * Normal chain, lifetime, EKU and (client side) hostname validation also apply.
 * Policies are immutable snapshots; multiple rules support explicit listener
 * admission of distinct applications/key rotations. There is no wildcard. */
typedef struct {
    uqg_bytes application_uri;
    uqg_bytes spki_sha256;
} uqg_peer;

typedef struct {
    uint32_t size;
    uint32_t timeout_ms;          /* 1..60000; all jobs have a finite deadline */
    uqg_bytes address;            /* numeric listener host:port; connector may use DNS */
    uqg_bytes alpn;               /* 1..255 UTF8 bytes, exact, no negotiation fallback */
    uqg_identity identity;
    uqg_peer *peers;
    uint32_t peer_count;          /* application-mTLS: 1..32; server-authenticated: 0, peers=NULL */
    uint32_t stream_limit;        /* 1..32 incoming streams per direction */
    uint32_t idle_timeout_ms;     /* 1000..600000 */
    uint32_t authentication_policy; /* enum uqg_authentication_policy */
} uqg_config;

/* Irrelevant fields must be zero. All inputs are copied before begin returns.
 * Listener/connection/stream begin exports accept only their own kind range. */
typedef struct {
    uint32_t size;
    uint32_t kind;
    uint32_t timeout_ms;
    uint32_t flags;
    uint64_t error_code;          /* <2^62 */
    uqg_bytes payload;
    uint32_t capacity;            /* read: 1..65536 */
    uint32_t index;               /* verified chain certificate index; zero = leaf */
} uqg_request;

typedef struct {
    uint32_t size;
    uint32_t state;               /* 1=pending; 2=terminal */
    uint32_t kind;
    uint32_t cancel_requested;
    int32_t status;
    int32_t native_status;        /* retained even if cancelled/timed out */
    uint32_t written;             /* actual native accepted bytes, even on failure */
    uint32_t payload_length;
    uqg_listener listener;
    uqg_connection connection;
    uqg_stream stream;
    uint64_t stream_id;
    uint64_t error_code;
    uint64_t final_size;          /* native sent/received reset, or sent high-water */
    uint64_t reliable_size;       /* native reset field; never inferred from Write */
    uint64_t reset_frame_type;    /* actual native encoder: 0x24 or ordinary 0x04 */
    uint32_t maximum_datagram_payload;
    uint32_t remote;
    uint32_t outcome;
    uint32_t reserved;
} uqg_result;

/* INFO payload. Certificate API returns the actual verified chain, including
 * the chosen anchor, not unverified peer-supplied extra certificates. */
typedef struct {
    uint32_t size;
    uint32_t tls_version;
    uint32_t is_client;
    uint32_t verified_chain_length;
    uint32_t handshake_complete;
    uint32_t used_0rtt;
    uint32_t resumed;
    uint32_t datagrams_local;
    uint32_t datagrams_remote;
    uint32_t reset09_local;
    uint32_t reset09_remote;
    uint32_t maximum_datagram_payload;
    uint64_t local_max_datagram_frame_size;
    uint64_t peer_max_datagram_frame_size;
    uint32_t alpn_length;
    uint32_t application_uri_length;
    uint8_t certificate_sha256[32];
    uint8_t spki_sha256[32];
    uint8_t alpn[256];
    uint8_t application_uri[512];
    uint32_t handshake_confirmed; /* client: received authenticated HANDSHAKE_DONE;
                                    server: completed native post-client-auth handshake */
    uint32_t mutual_tls_authenticated; /* supplied local certificate used and peer verified */
    uint32_t local_certificate_used; /* actual selection followed by completed/confirmed handshake */
    uint32_t authentication_policy; /* actual immutable admitted policy; no application identity for policy1 */
} uqg_connection_info;

typedef struct {
    uint32_t size;
    uint32_t operations;
    uint32_t terminal;
    uint32_t listeners;
    uint32_t connections;
    uint32_t streams;
    uint32_t native_connections;   /* includes bounded handshakes/unaccepted peers */
    uint32_t transports;
    uint64_t retained_bytes;
} uqg_counts;
typedef struct {
    uint32_t size;
    uint32_t version;
    uint32_t pointer_size;
    uint32_t config_size;
    uint32_t request_size;
    uint32_t result_size;
    uint32_t info_size;
    uint32_t counts_size;
} uqg_abi;

int32_t opcua_quic_abi(uqg_abi *abi);
int32_t opcua_quic_listen_begin(uqg_config *config, uqg_operation *operation);
int32_t opcua_quic_connect_begin(uqg_config *config, uqg_operation *operation);
int32_t opcua_quic_listener_begin(uqg_listener listener, uqg_request *request, uqg_operation *operation);
int32_t opcua_quic_connection_begin(uqg_connection connection, uqg_request *request, uqg_operation *operation);
int32_t opcua_quic_stream_begin(uqg_stream stream, uqg_request *request, uqg_operation *operation);
int32_t opcua_quic_poll(uqg_operation operation, uqg_result *result, uint8_t *payload, uint32_t capacity);
int32_t opcua_quic_cancel(uqg_operation operation, uint64_t application_error);
int32_t opcua_quic_release(uqg_operation operation);
int32_t opcua_quic_counts(uqg_counts *counts);

/* Pending release => PENDING; unobserved terminal release => BUSY. A short poll
 * buffer does NOT observe/consume a result. Successful terminal poll transfers
 * any new handle to the caller. Observe failures too, then release the job.
 * Cancellation requests interruption, not abandonment: poll to terminal even
 * after cancel, preserve bytes/handles, then release. Racing success stays success.
 * A prefix write defers cancellation through boundary marking; partial-prefix
 * failure closes that connection. RESET/RELEASE conflict with an active write.
 * STOP and CONNECTION_CLOSE may interrupt it. Close != release for connections;
 * release requires all children and referencing jobs to have been released.
 * Closing a listener leaves accepted connections alive.
 * STREAM_RELEASE_GRACEFUL closes the sending direction with FIN, cancels only
 * the unused receive direction, and releases the handle without CancelWrite.
 * The connection retains outstanding data/FIN until acknowledged or connection
 * closure. No referencing operation may remain; ordinary RELEASE is abortive.
 * STREAM_RESET_RECEIVED observes actual peer reset metadata independently of Read
 * and Write. It consumes no stream bytes and does not mean the reliable prefix
 * has already reached the application. It is distinct from peer STOP_SENDING.
 * STREAM_PEER_STOPPED observes the first authenticated peer STOP_SENDING even
 * after local send cancellation. Success is remote, with actual stream/error
 * and no reset metadata. Legacy WAIT_STOPPED observes the first send-context
 * cancellation cause and cannot prove a later peer STOP after local cancellation.
 * No function waits for network, TLS, native-job completion or a free queue slot. */
#ifdef __cplusplus
}
#endif
#endif
