// Copyright (c) OPC Foundation. Licensed under the MIT License.
package main

/*
#include "include/opcua_quic.h"
*/
import "C"

import (
	"context"
	"errors"
	"net"
	"net/netip"
	"strconv"
	"time"

	quic "github.com/quic-go/quic-go"
)

func nativeConfig(c *configuration) *quic.Config {
	return &quic.Config{
		Versions:             []quic.Version{quic.Version1},
		HandshakeIdleTimeout: 3 * time.Second,
		MaxIdleTimeout:       time.Duration(c.idleTimeout) * time.Millisecond,
		MaxIncomingStreams:   int64(c.streamLimit), MaxIncomingUniStreams: int64(c.streamLimit),
		InitialStreamReceiveWindow: 65536, MaxStreamReceiveWindow: 65536,
		InitialConnectionReceiveWindow: 262144, MaxConnectionReceiveWindow: 262144,
		InitialPacketSize: 1200,
		EnableDatagrams:   true, EnableStreamResetPartialDelivery: true, Allow0RTT: false,
		Tracer: connectionTrace,
	}
}

func (j *operation) listen(result *C.uqg_result) {
	tlsConfig, err := makeTLS(j.config, false)
	if err != nil {
		result.status = C.UQG_AUTHENTICATION
		return
	}
	addr, err := netip.ParseAddrPort(j.config.address)
	if err != nil {
		result.status = C.UQG_INVALID
		return
	}
	lc := net.ListenConfig{}
	socket, err := lc.ListenPacket(j.ctx, "udp", addr.String())
	if err != nil {
		result.status = errorStatus(err)
		return
	}
	owner := newOwner(socket)
	owner.transport.VerifySourceAddress = func(net.Addr) bool { return true }
	owner.transport.ConnContext = func(ctx context.Context, _ *quic.ClientInfo) (context.Context, error) {
		if !acquireNative() {
			return nil, errors.New("native connection admission limit")
		}
		owner.retain()
		context.AfterFunc(ctx, func() {
			owner.release()
			releaseNative()
		})
		return context.WithValue(ctx, traceKey{}, newTrace()), nil
	}
	native, err := owner.transport.Listen(tlsConfig, nativeConfig(j.config))
	if err != nil {
		owner.release()
		result.status = errorStatus(err)
		return
	}
	registry.Lock()
	id := nextHandle()
	if id != 0 && j.ctx.Err() == nil {
		registry.listeners[id] = &listener{id: id, native: native, owner: owner, config: j.config}
		result.listener.value = C.uint64_t(id)
		unreserve(j)
		registry.Unlock()
		return
	}
	registry.Unlock()
	closeUnaccepted(native)
	owner.release()
	result.status = C.UQG_CANCELLED
}

func resolveAddress(ctx context.Context, address string) (*net.UDPAddr, error) {
	if addr, err := netip.ParseAddrPort(address); err == nil && addr.Port() != 0 {
		return net.UDPAddrFromAddrPort(addr), nil
	}
	host, port, err := net.SplitHostPort(address)
	if err != nil || host == "" {
		return nil, errors.New("invalid remote address")
	}
	p, err := strconv.ParseUint(port, 10, 16)
	if err != nil || p == 0 {
		return nil, errors.New("invalid remote port")
	}
	ips, err := net.DefaultResolver.LookupNetIP(ctx, "ip", host)
	if err != nil || len(ips) == 0 {
		return nil, errors.New("remote address resolution failed")
	}
	return net.UDPAddrFromAddrPort(netip.AddrPortFrom(ips[0], uint16(p))), nil
}

func (j *operation) connect(result *C.uqg_result) {
	tlsConfig, err := makeTLS(j.config, true)
	if err != nil {
		result.status = C.UQG_AUTHENTICATION
		return
	}
	if !acquireNative() {
		result.status = C.UQG_LIMIT
		return
	}
	owned := true
	defer func() {
		if owned {
			releaseNative()
		}
	}()
	address, err := resolveAddress(j.ctx, j.config.address)
	if err != nil {
		result.status = errorStatus(err)
		return
	}
	network, local := "udp4", "0.0.0.0:0"
	if address.IP.To4() == nil {
		network, local = "udp6", "[::]:0"
	}
	lc := net.ListenConfig{}
	socket, err := lc.ListenPacket(j.ctx, network, local)
	if err != nil {
		result.status = errorStatus(err)
		return
	}
	owner := newOwner(socket)
	trace := newTrace()
	native, err := owner.transport.Dial(context.WithValue(j.ctx, traceKey{}, trace),
		address, tlsConfig, nativeConfig(j.config))
	if err != nil {
		owner.release()
		result.status = handshakeErrorStatus(err)
		return
	}
	owned = false
	context.AfterFunc(native.Context(), func() {
		owner.release()
		releaseNative()
	})
	j.saveConnection(native, trace, j.config, true, result)
}

func (j *operation) saveConnection(native *quic.Conn, trace *trace,
	config *configuration, client bool, result *C.uqg_result) {
	state := native.ConnectionState()
	uri, err := authorizedPeer(state.TLS, config.identity.peers)
	trace.mu.Lock()
	facts := trace.localSeen && trace.peerSeen && trace.local09 && trace.remote09 &&
		trace.localMax > 0 && trace.remoteMax > 0 && !trace.overflow
	trace.mu.Unlock()
	if err != nil || !state.TLS.HandshakeComplete || state.Used0RTT || state.TLS.DidResume {
		_ = native.CloseWithError(1, "unverified application identity")
		result.status = C.UQG_AUTHENTICATION
		return
	}
	if !facts || !state.SupportsDatagrams.Local || !state.SupportsDatagrams.Remote {
		_ = native.CloseWithError(1, "required transport capabilities unavailable")
		result.status = C.UQG_CAPABILITY
		return
	}
	confirmed := false
	localCertificateUsed := trace.localCertificateSelected.Load() && state.TLS.HandshakeComplete
	if !localCertificateUsed {
		_ = native.CloseWithError(1, "configured local certificate was not used")
		result.status = C.UQG_AUTHENTICATION
		return
	}
	if client {
		if !trace.clientCertificateRequested.Load() {
			_ = native.CloseWithError(1, "server did not request the configured client identity")
			result.status = C.UQG_AUTHENTICATION
			return
		}
		// TLS completion follows successful client Certificate/CertificateVerify/Finished
		// generation. Authenticated HANDSHAKE_DONE proves the server processed that flight.
		select {
		case <-j.ctx.Done():
			_ = native.CloseWithError(1, "handshake confirmation deadline")
			result.status = errorStatus(j.ctx.Err())
			return
		case <-native.Context().Done():
			result.status = handshakeErrorStatus(context.Cause(native.Context()))
			return
		case <-trace.handshakeDone:
			confirmed = true
		}
	} else {
		// quic-go closes this channel only after server handleHandshakeComplete has
		// called handleHandshakeConfirmed, following RequireAndVerifyClientCert.
		select {
		case <-native.HandshakeComplete():
			confirmed = state.TLS.HandshakeComplete
		default:
		}
	}
	if !confirmed || native.Context().Err() != nil {
		_ = native.CloseWithError(1, "confirmed mutual authentication required")
		result.status = C.UQG_AUTHENTICATION
		return
	}
	mutual := confirmed && state.TLS.HandshakeComplete && len(state.TLS.VerifiedChains) != 0 &&
		localCertificateUsed && (!client || trace.clientCertificateRequested.Load())
	registry.Lock()
	if len(registry.connections) < C.UQG_MAX_CONNECTIONS && j.ctx.Err() == nil &&
		(j.listener == nil || !j.listener.closed.Load()) {
		id := nextHandle()
		if id != 0 {
			registry.connections[id] = &connection{
				id: id, native: native, trace: trace, peerURI: uri, client: client,
				handshakeConfirmed: confirmed, mutualTLSAuthenticated: mutual,
				localCertificateUsed: localCertificateUsed,
			}
			result.connection.value = C.uint64_t(id)
			registry.Unlock()
			return
		}
	}
	registry.Unlock()
	_ = native.CloseWithError(1, "connection admission ended")
	result.status = C.UQG_LIMIT
}

func (j *operation) listenerOperation(result *C.uqg_result) []byte {
	l := j.listener
	switch j.request.kind {
	case C.UQG_LISTENER_ADDRESS:
		return []byte(l.native.Addr().String())
	case C.UQG_LISTENER_ACCEPT:
		native, err := l.native.Accept(j.ctx)
		if err != nil {
			result.status = errorStatus(err)
			return nil
		}
		t := native.Context().Value(traceKey{}).(*trace)
		j.saveConnection(native, t, l.config, false, result)
	case C.UQG_LISTENER_CLOSE:
		l.closed.Store(true)
		closeUnaccepted(l.native)
		l.owner.release()
		registry.Lock()
		delete(registry.listeners, l.id)
		registry.Unlock()
	}
	return nil
}

func closeUnaccepted(listener *quic.Listener) {
	_ = listener.Close()
	// This is also required when cancellation races listener publication:
	// completed but unaccepted handshakes must never outlive an unreachable listener.
	for {
		native, err := listener.Accept(context.Background())
		if err != nil {
			return
		}
		_ = native.CloseWithError(0, "listener closed before accept")
	}
}

func (j *operation) createStream(result *C.uqg_result) {
	c := j.connection
	s := &stream{connection: c}
	var err error
	if j.request.flags == 0 {
		var native *quic.Stream
		if j.request.kind == C.UQG_STREAM_ACCEPT {
			native, err = c.native.AcceptStream(j.ctx)
		} else {
			native, err = c.native.OpenStreamSync(j.ctx)
		}
		if err == nil {
			s.nativeID, s.send, s.receive = native.StreamID(), native, native
		}
	} else if j.request.kind == C.UQG_STREAM_ACCEPT {
		var native *quic.ReceiveStream
		native, err = c.native.AcceptUniStream(j.ctx)
		if err == nil {
			s.nativeID, s.receive = native.StreamID(), native
		}
	} else {
		var native *quic.SendStream
		native, err = c.native.OpenUniStreamSync(j.ctx)
		if err == nil {
			s.nativeID, s.send = native.StreamID(), native
		}
	}
	result.status = errorStatus(err)
	if err != nil {
		return
	}
	if !c.trace.register(uint64(s.nativeID)) {
		_ = c.native.CloseWithError(1, "stream evidence admission limit")
		result.status = C.UQG_LIMIT
		return
	}
	registry.Lock()
	if !c.released.Load() && c.native.Context().Err() == nil {
		id := nextHandle()
		if id != 0 {
			s.id = id
			registry.streams[id] = s
			result.stream.value, result.stream_id = C.uint64_t(id), C.uint64_t(s.nativeID)
			unreserve(j)
			registry.Unlock()
			return
		}
	}
	registry.Unlock()
	if s.send != nil {
		s.send.CancelWrite(0)
	}
	if s.receive != nil {
		s.receive.CancelRead(0)
	}
	c.trace.forget(uint64(s.nativeID))
	result.status = C.UQG_CLOSED
}
