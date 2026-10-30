// Copyright (c) OPC Foundation. Licensed under the MIT License.
package main

/*
#include "include/opcua_quic.h"
*/
import "C"

import (
	"context"
	"crypto/sha256"
	"errors"
	"io"
	"time"
	"unsafe"

	quic "github.com/quic-go/quic-go"
)

func (j *operation) run() {
	result := C.uqg_result{}
	var payload []byte
	defer func() {
		if recover() != nil {
			result.status = C.UQG_INTERNAL
			j.nativeStatusKnown = false
		}
		j.publish(result, payload)
	}()
	prefix := j.request.kind == C.UQG_STREAM_SEND && j.request.flags&C.UQG_SEND_RELIABLE_BOUNDARY != 0
	cleanup := j.request.kind == C.UQG_LISTENER_CLOSE || j.request.kind == C.UQG_CONNECTION_CLOSE ||
		j.request.kind == C.UQG_CONNECTION_RELEASE || j.request.kind == C.UQG_STREAM_RELEASE ||
		j.request.kind == C.UQG_STREAM_RELEASE_GRACEFUL
	if j.ctx.Err() != nil && !prefix && !cleanup {
		result.status = errorStatus(j.ctx.Err())
		return
	}
	switch {
	case j.request.kind == C.UQG_LISTEN:
		j.listen(&result)
	case j.request.kind == C.UQG_CONNECT:
		j.connect(&result)
	case j.listener != nil:
		payload = j.listenerOperation(&result)
	case j.stream != nil:
		payload = j.streamOperation(&result)
	case j.connection != nil:
		payload = j.connectionOperation(&result)
	default:
		result.status = C.UQG_INVALID
	}
}

func (j *operation) connectionOperation(result *C.uqg_result) []byte {
	c := j.connection
	switch j.request.kind {
	case C.UQG_CONNECTION_INFO:
		return c.information(result)
	case C.UQG_CONNECTION_CERTIFICATE:
		chains := c.native.ConnectionState().TLS.VerifiedChains
		if len(chains) == 0 {
			result.status = C.UQG_INVALID
			return nil
		}
		chain := chains[0]
		if int(j.request.index) >= len(chain) {
			result.status = C.UQG_INVALID
			return nil
		}
		data := chain[int(j.request.index)].Raw
		if len(data) > 16384 {
			result.status = C.UQG_LIMIT
			return nil
		}
		return append([]byte(nil), data...)
	case C.UQG_CONNECTION_EXPORTER:
		context := j.data
		if len(context) != 32 {
			result.status = C.UQG_INVALID
			return nil
		}
		tlsState := c.native.ConnectionState().TLS
		key, err := tlsState.ExportKeyingMaterial("EXPORTER-OPCUA-QUIC-DRAFT", context, 32)
		if err != nil || len(key) != 32 {
			clear(key)
			result.status = C.UQG_CAPABILITY
			return nil
		}
		return key
	case C.UQG_CONNECTION_EXPORTER_CUSTOM:
		if len(j.data) < 3 {
			result.status = C.UQG_INVALID
			return nil
		}
		labelLength := int(j.data[0])
		if labelLength == 0 || len(j.data) < 1+labelLength+2 {
			result.status = C.UQG_INVALID
			return nil
		}
		offset := 1 + labelLength
		contextLength := int(j.data[offset])
		offset++
		if len(j.data) != offset+contextLength+1 {
			result.status = C.UQG_INVALID
			return nil
		}
		outputLength := int(j.data[len(j.data)-1])
		if outputLength == 0 {
			result.status = C.UQG_INVALID
			return nil
		}
		for _, character := range j.data[1 : 1+labelLength] {
			if character < 0x20 || character > 0x7e {
				result.status = C.UQG_INVALID
				return nil
			}
		}
		label := string(j.data[1 : 1+labelLength])
		context := j.data[offset : offset+contextLength]
		tlsState := c.native.ConnectionState().TLS
		key, err := tlsState.ExportKeyingMaterial(label, context, outputLength)
		if err != nil || len(key) != outputLength {
			clear(key)
			result.status = C.UQG_CAPABILITY
			return nil
		}
		return key
	case C.UQG_STREAM_OPEN, C.UQG_STREAM_ACCEPT:
		j.createStream(result)
	case C.UQG_DATAGRAM_SEND:
		// quic-go's bounded datagram queue has no cancellable Add. A pending
		// cancellation closes this connection to unblock it; never abandon a job.
		stop := cancellationAction(j.ctx, func() {
			_ = c.native.CloseWithError(quic.ApplicationErrorCode(j.cancelCode.Load()),
				"datagram send interrupted")
		})
		err := c.native.SendDatagram(j.data)
		stop()
		result.status = errorStatus(err)
		var large *quic.DatagramTooLargeError
		if errors.As(err, &large) {
			result.maximum_datagram_payload = C.uint32_t(large.MaxDatagramPayloadSize)
		}
		if err == nil {
			result.written = C.uint32_t(len(j.data))
		}
	case C.UQG_DATAGRAM_RECEIVE:
		data, err := c.native.ReceiveDatagram(j.ctx)
		result.status = errorStatus(err)
		if len(data) > C.UQG_MAX_PAYLOAD {
			result.status = C.UQG_LIMIT
			return nil
		}
		result.written = C.uint32_t(len(data))
		return data
	case C.UQG_CONNECTION_CLOSE:
		result.status = errorStatus(c.native.CloseWithError(
			quic.ApplicationErrorCode(j.request.error_code), "application close"))
	case C.UQG_CONNECTION_WAIT_CLOSED:
		select {
		case <-j.ctx.Done():
			result.status = errorStatus(j.ctx.Err())
		case <-c.native.Context().Done():
			var closed *quic.ApplicationError
			if errors.As(context.Cause(c.native.Context()), &closed) {
				result.error_code = C.uint64_t(closed.ErrorCode)
				if closed.Remote {
					result.remote = 1
				}
			} else {
				result.status = C.UQG_CLOSED
			}
		}
	case C.UQG_CONNECTION_RELEASE:
		_ = c.native.CloseWithError(0, "connection released")
		registry.Lock()
		delete(registry.connections, c.id)
		registry.Unlock()
	}
	return nil
}

func (c *connection) information(result *C.uqg_result) []byte {
	state := c.native.ConnectionState()
	info := C.uqg_connection_info{
		size: C.sizeof_uqg_connection_info, tls_version: C.uint32_t(state.TLS.Version),
		authentication_policy: C.uint32_t(c.authenticationPolicy),
	}
	if len(state.TLS.VerifiedChains) != 0 {
		info.verified_chain_length = C.uint32_t(len(state.TLS.VerifiedChains[0]))
	}
	if state.TLS.HandshakeComplete {
		info.handshake_complete = 1
	}
	if state.Used0RTT {
		info.used_0rtt = 1
	}
	if state.TLS.DidResume {
		info.resumed = 1
	}
	if state.SupportsDatagrams.Local {
		info.datagrams_local = 1
	}
	if state.SupportsDatagrams.Remote {
		info.datagrams_remote = 1
	}
	if c.handshakeConfirmed {
		info.handshake_confirmed = 1
	}
	if c.mutualTLSAuthenticated {
		info.mutual_tls_authenticated = 1
	}
	if c.localCertificateUsed {
		info.local_certificate_used = 1
	}
	if c.client {
		info.is_client = 1
	}
	c.trace.mu.Lock()
	info.local_max_datagram_frame_size = C.uint64_t(c.trace.localMax)
	info.peer_max_datagram_frame_size = C.uint64_t(c.trace.remoteMax)
	if c.trace.local09 {
		info.reset09_local = 1
	}
	if c.trace.remote09 {
		info.reset09_remote = 1
	}
	c.trace.mu.Unlock()
	if state.SupportsDatagrams.Local && state.SupportsDatagrams.Remote {
		var tooLarge *quic.DatagramTooLargeError
		// Native SendDatagram rejects before queue admission; this never sends a probe.
		if err := c.native.SendDatagram(make([]byte, C.UQG_MAX_PAYLOAD)); errors.As(err, &tooLarge) {
			info.maximum_datagram_payload = C.uint32_t(tooLarge.MaxDatagramPayloadSize)
		} else {
			result.status = errorStatus(err)
			if err == nil {
				result.status = C.UQG_INTERNAL
			}
			return nil
		}
	}
	if len(state.TLS.VerifiedChains) != 0 && len(state.TLS.VerifiedChains[0]) != 0 {
		leaf := state.TLS.VerifiedChains[0][0]
		certHash, keyHash := sha256.Sum256(leaf.Raw), sha256.Sum256(leaf.RawSubjectPublicKeyInfo)
		copy(unsafe.Slice((*byte)(unsafe.Pointer(&info.certificate_sha256[0])), 32), certHash[:])
		copy(unsafe.Slice((*byte)(unsafe.Pointer(&info.spki_sha256[0])), 32), keyHash[:])
	}
	protocol := []byte(state.TLS.NegotiatedProtocol)
	uri := []byte(c.peerURI)
	if len(protocol) > len(info.alpn) || len(uri) > len(info.application_uri) {
		result.status = C.UQG_INTERNAL
		return nil
	}
	info.alpn_length, info.application_uri_length = C.uint32_t(len(protocol)), C.uint32_t(len(uri))
	copy(unsafe.Slice((*byte)(unsafe.Pointer(&info.alpn[0])), len(info.alpn)), protocol)
	copy(unsafe.Slice((*byte)(unsafe.Pointer(&info.application_uri[0])), len(info.application_uri)), uri)
	return append([]byte(nil), unsafe.Slice((*byte)(unsafe.Pointer(&info)), C.sizeof_uqg_connection_info)...)
}

func streamError(result *C.uqg_result, err error) {
	result.status = errorStatus(err)
	var e *quic.StreamError
	if errors.As(err, &e) {
		result.error_code = C.uint64_t(e.ErrorCode)
		if e.Remote {
			result.remote = 1
		}
	}
}

func (j *operation) send(result *C.uqg_result) {
	s := j.stream
	_ = s.send.SetWriteDeadline(j.deadline)
	prefix := j.request.flags&C.UQG_SEND_RELIABLE_BOUNDARY != 0
	if !prefix {
		stop := cancellationAction(j.ctx, func() {
			if errors.Is(j.ctx.Err(), context.Canceled) {
				s.send.CancelWrite(quic.StreamErrorCode(j.cancelCode.Load()))
			} else {
				_ = s.send.SetWriteDeadline(time.Now())
			}
		})
		defer stop()
	}
	n, err := s.send.Write(j.data)
	result.written = C.uint32_t(n)
	streamError(result, err)
	result.native_status = result.status
	j.nativeStatusKnown = true
	if prefix {
		if err == nil && n == len(j.data) {
			s.send.SetReliableBoundary()
			if j.ctx.Err() != nil {
				s.send.CancelWrite(quic.StreamErrorCode(j.cancelCode.Load()))
				result.status, result.error_code = C.UQG_CANCELLED, C.uint64_t(j.cancelCode.Load())
			}
		} else {
			_ = s.connection.native.CloseWithError(1, "incomplete reliable boundary")
		}
	}
	if result.status == 0 && j.request.flags&C.UQG_SEND_FIN != 0 {
		streamError(result, s.send.Close())
		result.native_status = result.status
	}
}

func (j *operation) resetEvidence(sent bool, result *C.uqg_result) {
	ticker := time.NewTicker(time.Millisecond)
	defer ticker.Stop()
	for {
		e, ok := j.connection.trace.evidence(uint64(j.stream.nativeID))
		r := e.received
		if sent {
			r = e.sent
		}
		if ok && r.present {
			result.final_size, result.reliable_size = C.uint64_t(r.final), C.uint64_t(r.reliable)
			result.error_code, result.reset_frame_type = C.uint64_t(r.code), C.uint64_t(r.frame)
			result.outcome = C.UQG_READ_RESET
			return
		}
		select {
		case <-j.ctx.Done():
			result.status = errorStatus(j.ctx.Err())
			return
		case <-ticker.C:
		}
	}
}

func (j *operation) read(result *C.uqg_result) []byte {
	s := j.stream
	_ = s.receive.SetReadDeadline(j.deadline)
	stop := cancellationAction(j.ctx, func() { _ = s.receive.SetReadDeadline(time.Now()) })
	defer stop()
	buffer := make([]byte, int(j.request.capacity))
	n, err := s.receive.Read(buffer)
	result.written = C.uint32_t(n)
	var reset *quic.StreamError
	if errors.Is(err, io.EOF) {
		result.outcome = C.UQG_READ_FIN
	} else if errors.As(err, &reset) && reset.Remote {
		result.remote, result.error_code = 1, C.uint64_t(reset.ErrorCode)
		j.resetEvidence(false, result)
	} else {
		streamError(result, err)
	}
	return buffer[:n]
}

func (j *operation) peerStopped(result *C.uqg_result) {
	ticker := time.NewTicker(time.Millisecond)
	defer ticker.Stop()
	for {
		e, ok := j.connection.trace.evidence(uint64(j.stream.nativeID))
		if ok && e.peerStop.present {
			result.remote, result.error_code = 1, C.uint64_t(e.peerStop.code)
			return
		}
		select {
		case <-j.ctx.Done():
			result.status = errorStatus(j.ctx.Err())
			return
		case <-j.connection.native.Context().Done():
			result.status = C.UQG_CLOSED
			return
		case <-ticker.C:
		}
	}
}

func (j *operation) streamOperation(result *C.uqg_result) []byte {
	s := j.stream
	result.stream_id = C.uint64_t(s.nativeID)
	switch j.request.kind {
	case C.UQG_STREAM_SEND:
		j.send(result)
	case C.UQG_STREAM_READ:
		return j.read(result)
	case C.UQG_STREAM_RESET:
		s.send.CancelWrite(quic.StreamErrorCode(j.request.error_code))
	case C.UQG_STREAM_RESET_SENT:
		j.resetEvidence(true, result)
	case C.UQG_STREAM_RESET_RECEIVED:
		j.resetEvidence(false, result)
		if result.status == C.UQG_OK {
			result.remote = 1
		}
	case C.UQG_STREAM_STOP:
		s.receive.CancelRead(quic.StreamErrorCode(j.request.error_code))
	case C.UQG_STREAM_WAIT_STOPPED:
		select {
		case <-j.ctx.Done():
			result.status = errorStatus(j.ctx.Err())
		case <-s.send.Context().Done():
			streamError(result, context.Cause(s.send.Context()))
			if result.error_code != 0 || result.remote != 0 {
				result.status = 0
			}
		}
	case C.UQG_STREAM_PEER_STOPPED:
		j.peerStopped(result)
	case C.UQG_STREAM_SENT_OFFSET:
		e, ok := j.connection.trace.evidence(uint64(s.nativeID))
		if !ok {
			result.status = C.UQG_INTERNAL
		} else {
			result.final_size = C.uint64_t(e.sentOffset)
		}
	case C.UQG_STREAM_RELEASE, C.UQG_STREAM_RELEASE_GRACEFUL:
		if s.send != nil {
			if j.request.kind == C.UQG_STREAM_RELEASE_GRACEFUL {
				streamError(result, s.send.Close())
			} else {
				s.send.CancelWrite(quic.StreamErrorCode(j.request.error_code))
			}
		}
		if s.receive != nil {
			s.receive.CancelRead(quic.StreamErrorCode(j.request.error_code))
		}
		j.connection.trace.forget(uint64(s.nativeID))
		registry.Lock()
		delete(registry.streams, s.id)
		registry.Unlock()
	}
	return nil
}
