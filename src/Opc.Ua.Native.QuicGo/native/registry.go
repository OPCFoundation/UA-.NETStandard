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
	"sync"
	"sync/atomic"
	"time"

	quic "github.com/quic-go/quic-go"
)

type sender interface {
	Write([]byte) (int, error)
	Close() error
	CancelWrite(quic.StreamErrorCode)
	SetWriteDeadline(time.Time) error
	SetReliableBoundary()
	Context() context.Context
}

type receiver interface {
	Read([]byte) (int, error)
	CancelRead(quic.StreamErrorCode)
	SetReadDeadline(time.Time) error
}

// socketOwner separates listener lifetime from accepted connection lifetime.
// Releasing the listener's reference never closes an accepted connection.
type socketOwner struct {
	mu        sync.Mutex
	refs      int
	transport *quic.Transport
	socket    net.PacketConn
}

func (o *socketOwner) retain() {
	o.mu.Lock()
	o.refs++
	o.mu.Unlock()
}

func (o *socketOwner) release() {
	o.mu.Lock()
	o.refs--
	last := o.refs == 0
	o.mu.Unlock()
	if last {
		_ = o.transport.Close()
		_ = o.socket.Close()
		registry.Lock()
		registry.transports--
		registry.Unlock()
	}
}

type listener struct {
	id     uint64
	native *quic.Listener
	owner  *socketOwner
	config *configuration
	closed atomic.Bool
}

type connection struct {
	id                     uint64
	native                 *quic.Conn
	trace                  *trace
	peerURI                string
	authenticationPolicy   uint32
	client                 bool
	handshakeConfirmed     bool
	mutualTLSAuthenticated bool
	localCertificateUsed   bool
	released               atomic.Bool
}

type stream struct {
	id         uint64
	nativeID   quic.StreamID
	connection *connection
	send       sender
	receive    receiver
	released   atomic.Bool
}

type conflictKey struct {
	handle uint64
	group  uint32
}

type operation struct {
	id                uint64
	request           C.uqg_request
	config            *configuration
	listener          *listener
	connection        *connection
	stream            *stream
	key               conflictKey
	bytes             uint64
	data              []byte
	ctx               context.Context
	cancel            context.CancelFunc
	deadline          time.Time
	cancelled         atomic.Bool
	cancelCode        atomic.Uint64
	reserveKind       uint32
	nativeStatusKnown bool
	// Published under registry.Mutex. Terminal fields are immutable.
	done, observed bool
	result         C.uqg_result
	payload        []byte
}

var registry = struct {
	sync.Mutex
	next              uint64
	retained          uint64
	nativeConnections uint32
	transports        uint32
	reservedListeners uint32
	reservedStreams   uint32
	operations        map[uint64]*operation
	listeners         map[uint64]*listener
	connections       map[uint64]*connection
	streams           map[uint64]*stream
	conflicts         map[conflictKey]bool
}{
	operations: make(map[uint64]*operation),
	listeners:  make(map[uint64]*listener), connections: make(map[uint64]*connection),
	streams: make(map[uint64]*stream), conflicts: make(map[conflictKey]bool),
}

func nextHandle() uint64 {
	// Never reuse IDs, including after failed creation. Exhaustion fails closed.
	if registry.next == ^uint64(0) {
		return 0
	}
	registry.next++
	return registry.next
}

// admit is called with the registry locked; there is no waiting admission queue.
func admit(j *operation) C.int32_t {
	if len(registry.operations) == C.UQG_MAX_OPERATIONS ||
		registry.retained+j.bytes > C.UQG_MAX_RETAINED_BYTES {
		return C.UQG_LIMIT
	}
	if j.key.group != 0 && registry.conflicts[j.key] {
		return C.UQG_BUSY
	}
	switch j.request.kind {
	case C.UQG_LISTEN:
		if len(registry.listeners)+int(registry.reservedListeners) == C.UQG_MAX_LISTENERS {
			return C.UQG_LIMIT
		}
		registry.reservedListeners++
		j.reserveKind = 1
	case C.UQG_STREAM_OPEN, C.UQG_STREAM_ACCEPT:
		if len(registry.streams)+int(registry.reservedStreams) == C.UQG_MAX_STREAMS {
			return C.UQG_LIMIT
		}
		registry.reservedStreams++
		j.reserveKind = 2
	}
	j.id = nextHandle()
	if j.id == 0 {
		unreserve(j)
		return C.UQG_LIMIT
	}
	j.deadline = time.Now().Add(time.Duration(j.request.timeout_ms) * time.Millisecond)
	j.ctx, j.cancel = context.WithDeadline(context.Background(), j.deadline)
	registry.operations[j.id] = j
	registry.retained += j.bytes
	if j.key.group != 0 {
		registry.conflicts[j.key] = true
	}
	return C.UQG_OK
}

func unreserve(j *operation) {
	if j.reserveKind == 1 {
		registry.reservedListeners--
	} else if j.reserveKind == 2 {
		registry.reservedStreams--
	}
	j.reserveKind = 0
}

func acquireNative() bool {
	registry.Lock()
	defer registry.Unlock()
	if registry.nativeConnections == C.UQG_MAX_CONNECTIONS {
		return false
	}
	registry.nativeConnections++
	return true
}

func releaseNative() {
	registry.Lock()
	registry.nativeConnections--
	registry.Unlock()
}

func newOwner(socket net.PacketConn) *socketOwner {
	o := &socketOwner{refs: 1, socket: socket, transport: &quic.Transport{Conn: socket}}
	registry.Lock()
	registry.transports++
	registry.Unlock()
	return o
}

func errorStatus(err error) C.int32_t {
	if err == nil {
		return C.UQG_OK
	}
	if errors.Is(err, context.Canceled) {
		return C.UQG_CANCELLED
	}
	var netError net.Error
	if errors.Is(err, context.DeadlineExceeded) || (errors.As(err, &netError) && netError.Timeout()) {
		return C.UQG_TIMEOUT
	}
	var large *quic.DatagramTooLargeError
	if errors.As(err, &large) {
		return C.UQG_TOO_LARGE
	}
	return C.UQG_CLOSED
}

func handshakeErrorStatus(err error) C.int32_t {
	var transportError *quic.TransportError
	if errors.As(err, &transportError) && transportError.ErrorCode >= 0x100 &&
		transportError.ErrorCode < 0x200 {
		return C.UQG_AUTHENTICATION
	}
	return errorStatus(err)
}

func (j *operation) publish(result C.uqg_result, payload []byte) {
	clear(j.data)
	j.data = nil
	if j.config != nil {
		clear(j.config.identity.key)
	}
	result.size, result.state, result.kind = C.sizeof_uqg_result, 2, j.request.kind
	if !j.nativeStatusKnown {
		result.native_status = result.status
	}
	if result.status != 0 && j.ctx.Err() != nil {
		result.status = errorStatus(j.ctx.Err())
	}
	result.payload_length = C.uint32_t(len(payload))
	registry.Lock()
	if j.cancelled.Load() {
		result.cancel_requested = 1
	}
	unreserve(j)
	j.result, j.payload, j.done = result, payload, true
	delete(registry.conflicts, j.key)
	registry.Unlock()
	j.cancel()
}

func cancellationAction(ctx context.Context, action func()) func() {
	finished := make(chan struct{})
	stop := context.AfterFunc(ctx, func() {
		defer close(finished)
		action()
	})
	return func() {
		if !stop() {
			<-finished
		}
	}
}

func referencingJobs(j *operation, conn *connection, s *stream) bool {
	for _, other := range registry.operations {
		if other == j {
			continue
		}
		if (s != nil && other.stream == s) || (conn != nil && other.connection == conn) {
			return true
		}
	}
	return false
}
