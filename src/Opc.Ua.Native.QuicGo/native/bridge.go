// Copyright (c) OPC Foundation. Licensed under the MIT License.
package main

/*
#cgo linux LDFLAGS: -Wl,--version-script=${SRCDIR}/include/exports.map
#include "include/opcua_quic.h"
*/
import "C"

import (
	"net/url"
	"unicode/utf8"
	"unsafe"
)

func main() {}

func guard(status *C.int32_t) {
	if recover() != nil {
		*status = C.UQG_INTERNAL
	}
}

func validBytes(b C.uqg_bytes, maximum uint32, required bool) bool {
	return b.reserved == 0 && uint32(b.length) <= maximum &&
		(!required || b.length > 0) && (b.length == 0 || b.data != nil)
}

func copyBytes(b C.uqg_bytes) []byte {
	if b.length == 0 {
		return nil
	}
	return C.GoBytes(unsafe.Pointer(b.data), C.int(b.length))
}

func text(b C.uqg_bytes) (string, bool) {
	p := copyBytes(b)
	if !utf8.Valid(p) {
		return "", false
	}
	s := string(p)
	for _, r := range s {
		if r <= 0x20 || r == 0x7f {
			return "", false
		}
	}
	return s, true
}

func validURI(s string) bool {
	u, err := url.Parse(s)
	return err == nil && u.IsAbs() && u.Scheme != "" && s != u.Scheme+":"
}

func validateConfig(c *C.uqg_config, client bool) (uint64, bool) {
	if c == nil || c.size != C.sizeof_uqg_config || c.reserved != 0 ||
		c.timeout_ms == 0 || c.timeout_ms > 60000 || c.stream_limit == 0 || c.stream_limit > 32 ||
		c.idle_timeout_ms < 1000 || c.idle_timeout_ms > 600000 ||
		c.peer_count == 0 || c.peer_count > C.UQG_MAX_PEERS || c.peers == nil ||
		!validBytes(c.address, 512, true) || !validBytes(c.alpn, 255, true) {
		return 0, false
	}
	m := &c.identity
	blobs := []C.uqg_bytes{m.certificate_der, m.issuer_chain_der, m.private_key_pkcs8, m.trust_roots_der}
	total := uint64(c.address.length) + uint64(c.alpn.length) + uint64(m.local_uri.length) + uint64(m.server_name.length)
	for i, blob := range blobs {
		if !validBytes(blob, 16384, i != 1) {
			return 0, false
		}
		total += uint64(blob.length)
	}
	if !validBytes(m.local_uri, 512, true) || !validBytes(m.server_name, 253, client) {
		return 0, false
	}
	for _, peer := range unsafe.Slice(c.peers, int(c.peer_count)) {
		if !validBytes(peer.application_uri, 512, true) ||
			!validBytes(peer.spki_sha256, 32, true) || peer.spki_sha256.length != 32 {
			return 0, false
		}
		total += uint64(peer.application_uri.length) + 32
	}
	return 2*total + 65536, true
}

func copyConfig(c *C.uqg_config) (*configuration, bool) {
	cfg := &configuration{streamLimit: uint32(c.stream_limit), idleTimeout: uint32(c.idle_timeout_ms)}
	m := &c.identity
	var ok bool
	if cfg.address, ok = text(c.address); !ok {
		return nil, false
	}
	if cfg.alpn, ok = text(c.alpn); !ok {
		return nil, false
	}
	if cfg.identity.localURI, ok = text(m.local_uri); !ok || !validURI(cfg.identity.localURI) {
		return nil, false
	}
	if cfg.identity.serverName, ok = text(m.server_name); !ok {
		return nil, false
	}
	for _, peer := range unsafe.Slice(c.peers, int(c.peer_count)) {
		uri, valid := text(peer.application_uri)
		if !valid || !validURI(uri) {
			return nil, false
		}
		var pin [32]byte
		copy(pin[:], copyBytes(peer.spki_sha256))
		if pin == [32]byte{} {
			return nil, false
		}
		cfg.identity.peers = append(cfg.identity.peers, peerRule{uri, pin})
	}
	cfg.identity.certificate, cfg.identity.issuers = copyBytes(m.certificate_der), copyBytes(m.issuer_chain_der)
	cfg.identity.roots, cfg.identity.key = copyBytes(m.trust_roots_der), copyBytes(m.private_key_pkcs8)
	return cfg, true
}

func beginConfiguration(c *C.uqg_config, out *C.uqg_operation, client bool) C.int32_t {
	if out == nil {
		return C.UQG_INVALID
	}
	out.value = 0
	budget, ok := validateConfig(c, client)
	if !ok {
		return C.UQG_INVALID
	}
	kind := C.uint32_t(C.UQG_LISTEN)
	if client {
		kind = C.UQG_CONNECT
	}
	j := &operation{request: C.uqg_request{kind: kind, timeout_ms: c.timeout_ms}, bytes: budget}
	registry.Lock()
	status := admit(j)
	registry.Unlock()
	if status != 0 {
		return status
	}
	j.config, ok = copyConfig(c)
	if !ok {
		j.cancel()
		registry.Lock()
		unreserve(j)
		delete(registry.operations, j.id)
		registry.retained -= j.bytes
		registry.Unlock()
		return C.UQG_INVALID
	}
	out.value = C.uint64_t(j.id)
	go j.run()
	return C.UQG_OK
}

//export opcua_quic_abi
func opcua_quic_abi(abi *C.uqg_abi) C.int32_t {
	if abi == nil || abi.size != C.sizeof_uqg_abi {
		return C.UQG_INVALID
	}
	*abi = C.uqg_abi{size: C.sizeof_uqg_abi, version: 4, pointer_size: C.uint32_t(unsafe.Sizeof(uintptr(0))),
		config_size: C.sizeof_uqg_config, request_size: C.sizeof_uqg_request,
		result_size: C.sizeof_uqg_result, info_size: C.sizeof_uqg_connection_info, counts_size: C.sizeof_uqg_counts}
	return C.UQG_OK
}

//export opcua_quic_listen_begin
func opcua_quic_listen_begin(config *C.uqg_config, out *C.uqg_operation) (status C.int32_t) {
	defer guard(&status)
	return beginConfiguration(config, out, false)
}

//export opcua_quic_connect_begin
func opcua_quic_connect_begin(config *C.uqg_config, out *C.uqg_operation) (status C.int32_t) {
	defer guard(&status)
	return beginConfiguration(config, out, true)
}

func validRequest(r *C.uqg_request) bool {
	if r == nil || r.size != C.sizeof_uqg_request || r.timeout_ms == 0 || r.timeout_ms > 60000 ||
		!validBytes(r.payload, C.UQG_MAX_PAYLOAD, false) || r.capacity > C.UQG_MAX_PAYLOAD ||
		uint64(r.error_code) >= 1<<62 {
		return false
	}
	if r.kind != C.UQG_STREAM_SEND && r.kind != C.UQG_DATAGRAM_SEND && r.payload.length != 0 {
		return false
	}
	if (r.kind == C.UQG_STREAM_READ && r.capacity == 0) || (r.kind != C.UQG_STREAM_READ && r.capacity != 0) {
		return false
	}
	if r.kind != C.UQG_CONNECTION_CERTIFICATE && r.index != 0 {
		return false
	}
	if r.error_code != 0 && r.kind != C.UQG_STREAM_RESET && r.kind != C.UQG_STREAM_STOP &&
		r.kind != C.UQG_CONNECTION_CLOSE && r.kind != C.UQG_STREAM_RELEASE {
		return false
	}
	switch r.kind {
	case C.UQG_STREAM_OPEN, C.UQG_STREAM_ACCEPT:
		return r.flags <= 1
	case C.UQG_STREAM_SEND:
		return r.flags&^3 == 0 && (r.flags&C.UQG_SEND_RELIABLE_BOUNDARY == 0 || r.payload.length > 0)
	default:
		return r.flags == 0
	}
}

func beginResource(id uint64, category uint32, r *C.uqg_request, out *C.uqg_operation) C.int32_t {
	if out == nil {
		return C.UQG_INVALID
	}
	out.value = 0
	if !validRequest(r) {
		return C.UQG_INVALID
	}
	j := &operation{request: *r, bytes: uint64(r.payload.length) + uint64(r.capacity) + 1024}
	j.request.payload.data = nil
	registry.Lock()
	defer registry.Unlock()
	switch category {
	case 1:
		j.listener = registry.listeners[id]
		if j.listener == nil || j.listener.closed.Load() ||
			r.kind < C.UQG_LISTENER_ACCEPT || r.kind > C.UQG_LISTENER_CLOSE {
			return C.UQG_INVALID
		}
		j.key = conflictKey{id, uint32(r.kind)}
	case 2:
		j.connection = registry.connections[id]
		if j.connection == nil || j.connection.released.Load() ||
			r.kind < C.UQG_CONNECTION_INFO || r.kind > C.UQG_CONNECTION_RELEASE {
			return C.UQG_INVALID
		}
		if r.kind == C.UQG_CONNECTION_RELEASE {
			if referencingJobs(j, j.connection, nil) {
				return C.UQG_BUSY
			}
			for _, s := range registry.streams {
				if s.connection == j.connection {
					return C.UQG_BUSY
				}
			}
		}
		j.key = conflictKey{id, uint32(r.kind)*2 + uint32(r.flags)}
		if r.kind == C.UQG_DATAGRAM_RECEIVE || r.kind == C.UQG_CONNECTION_INFO {
			j.bytes += C.UQG_MAX_PAYLOAD
		} else if r.kind == C.UQG_CONNECTION_CERTIFICATE {
			j.bytes += 16384
		}
	case 3:
		j.stream = registry.streams[id]
		if j.stream == nil || j.stream.released.Load() ||
			r.kind < C.UQG_STREAM_SEND || r.kind > C.UQG_STREAM_PEER_STOPPED {
			return C.UQG_INVALID
		}
		j.connection = j.stream.connection
		if ((r.kind == C.UQG_STREAM_SEND || r.kind == C.UQG_STREAM_RESET ||
			r.kind == C.UQG_STREAM_RESET_SENT || r.kind == C.UQG_STREAM_WAIT_STOPPED ||
			r.kind == C.UQG_STREAM_SENT_OFFSET || r.kind == C.UQG_STREAM_PEER_STOPPED) && j.stream.send == nil) ||
			((r.kind == C.UQG_STREAM_READ || r.kind == C.UQG_STREAM_STOP ||
				r.kind == C.UQG_STREAM_RESET_RECEIVED) && j.stream.receive == nil) {
			return C.UQG_INVALID
		}
		j.key = conflictKey{id, uint32(r.kind)}
		if r.kind == C.UQG_STREAM_RESET {
			j.key.group = C.UQG_STREAM_SEND
		}
		if r.kind == C.UQG_STREAM_RELEASE && referencingJobs(j, nil, j.stream) {
			return C.UQG_BUSY
		}
	default:
		return C.UQG_INVALID
	}
	if status := admit(j); status != 0 {
		return status
	}
	j.data = copyBytes(r.payload)
	if r.kind == C.UQG_CONNECTION_RELEASE {
		j.connection.released.Store(true)
	} else if r.kind == C.UQG_STREAM_RELEASE {
		j.stream.released.Store(true)
	} else if r.kind == C.UQG_LISTENER_CLOSE {
		j.listener.closed.Store(true)
	}
	out.value = C.uint64_t(j.id)
	go j.run()
	return C.UQG_OK
}

//export opcua_quic_listener_begin
func opcua_quic_listener_begin(handle C.uqg_listener, request *C.uqg_request,
	out *C.uqg_operation) (status C.int32_t) {
	defer guard(&status)
	return beginResource(uint64(handle.value), 1, request, out)
}

//export opcua_quic_connection_begin
func opcua_quic_connection_begin(handle C.uqg_connection, request *C.uqg_request,
	out *C.uqg_operation) (status C.int32_t) {
	defer guard(&status)
	return beginResource(uint64(handle.value), 2, request, out)
}

//export opcua_quic_stream_begin
func opcua_quic_stream_begin(handle C.uqg_stream, request *C.uqg_request,
	out *C.uqg_operation) (status C.int32_t) {
	defer guard(&status)
	return beginResource(uint64(handle.value), 3, request, out)
}

//export opcua_quic_poll
func opcua_quic_poll(handle C.uqg_operation, result *C.uqg_result,
	payload *C.uint8_t, capacity C.uint32_t) (status C.int32_t) {
	defer guard(&status)
	if result == nil || result.size != C.sizeof_uqg_result || capacity > C.UQG_MAX_PAYLOAD ||
		(capacity > 0 && payload == nil) {
		return C.UQG_INVALID
	}
	registry.Lock()
	defer registry.Unlock()
	j := registry.operations[uint64(handle.value)]
	if j == nil {
		return C.UQG_INVALID
	}
	if !j.done {
		*result = C.uqg_result{size: C.sizeof_uqg_result, state: 1, kind: j.request.kind}
		return C.UQG_PENDING
	}
	*result = j.result
	if len(j.payload) > int(capacity) {
		return C.UQG_BUFFER_SMALL
	}
	if len(j.payload) > 0 {
		copy(unsafe.Slice((*byte)(unsafe.Pointer(payload)), int(capacity)), j.payload)
	}
	j.observed = true
	return C.UQG_OK
}

//export opcua_quic_cancel
func opcua_quic_cancel(handle C.uqg_operation, code C.uint64_t) (status C.int32_t) {
	defer guard(&status)
	if uint64(code) >= 1<<62 {
		return C.UQG_INVALID
	}
	registry.Lock()
	j := registry.operations[uint64(handle.value)]
	if j == nil {
		registry.Unlock()
		return C.UQG_INVALID
	}
	if !j.done && !j.cancelled.Load() {
		j.cancelCode.Store(uint64(code))
		j.cancelled.Store(true)
	}
	done := j.done
	registry.Unlock()
	if !done {
		j.cancel()
	}
	return C.UQG_OK
}

//export opcua_quic_release
func opcua_quic_release(handle C.uqg_operation) (status C.int32_t) {
	defer guard(&status)
	registry.Lock()
	defer registry.Unlock()
	j := registry.operations[uint64(handle.value)]
	if j == nil {
		return C.UQG_INVALID
	}
	if !j.done {
		return C.UQG_PENDING
	}
	if !j.observed {
		return C.UQG_BUSY
	}
	clear(j.payload)
	j.payload = nil
	registry.retained -= j.bytes
	delete(registry.operations, uint64(handle.value))
	return C.UQG_OK
}

//export opcua_quic_counts
func opcua_quic_counts(counts *C.uqg_counts) (status C.int32_t) {
	defer guard(&status)
	if counts == nil || counts.size != C.sizeof_uqg_counts {
		return C.UQG_INVALID
	}
	registry.Lock()
	defer registry.Unlock()
	*counts = C.uqg_counts{
		size: C.sizeof_uqg_counts, operations: C.uint32_t(len(registry.operations)),
		listeners: C.uint32_t(len(registry.listeners)), connections: C.uint32_t(len(registry.connections)),
		streams: C.uint32_t(len(registry.streams)), retained_bytes: C.uint64_t(registry.retained),
		native_connections: C.uint32_t(registry.nativeConnections), transports: C.uint32_t(registry.transports),
	}
	for _, j := range registry.operations {
		if j.done {
			counts.terminal++
		}
	}
	return C.UQG_OK
}
