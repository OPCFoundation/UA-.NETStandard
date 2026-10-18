// Copyright (c) OPC Foundation. Licensed under the MIT License.
package main

import (
	"context"
	"sync"
	"sync/atomic"

	quic "github.com/quic-go/quic-go"
	"github.com/quic-go/quic-go/qlog"
	"github.com/quic-go/quic-go/qlogwriter"
)

type traceKey struct{}

type resetEvidence struct {
	final, reliable, code, frame uint64
	present                      bool
}

type stopEvidence struct {
	code    uint64
	present bool
}

type streamEvidence struct {
	sentOffset     uint64
	sent, received resetEvidence
	peerStop       stopEvidence
}

// trace retains facts, not an ever-growing packet/qlog log. Released stream IDs
// below each allocator high-water are ignored even on late retransmission.
type trace struct {
	mu                         sync.Mutex
	localMax, remoteMax        uint64
	local09, remote09          bool
	localSeen, peerSeen        bool
	overflow                   bool
	registered                 [4]uint64
	registeredAny              [4]bool
	streams                    map[uint64]*streamEvidence
	handshakeDoneReceived      bool
	handshakeDone              chan struct{}
	clientCertificateRequested atomic.Bool
	localCertificateSelected   atomic.Bool
}

func newTrace() *trace {
	return &trace{streams: make(map[uint64]*streamEvidence), handshakeDone: make(chan struct{})}
}
func (t *trace) AddProducer() qlogwriter.Recorder { return t }
func (t *trace) SupportsSchemas(string) bool      { return true }
func (t *trace) Close() error                     { return nil }

func (t *trace) get(id uint64) *streamEvidence {
	if e := t.streams[id]; e != nil {
		return e
	}
	category := id & 3
	if t.registeredAny[category] && id <= t.registered[category] {
		return nil
	}
	if len(t.streams) == 256 {
		t.overflow = true
		return nil
	}
	e := &streamEvidence{}
	t.streams[id] = e
	return e
}

func (t *trace) register(id uint64) bool {
	t.mu.Lock()
	defer t.mu.Unlock()
	e := t.get(id)
	if e == nil || t.overflow {
		return false
	}
	category := id & 3
	t.registeredAny[category] = true
	if id > t.registered[category] {
		t.registered[category] = id
	}
	return true
}

func (t *trace) forget(id uint64) {
	t.mu.Lock()
	delete(t.streams, id)
	t.mu.Unlock()
}

func (t *trace) evidence(id uint64) (streamEvidence, bool) {
	t.mu.Lock()
	defer t.mu.Unlock()
	e, ok := t.streams[id]
	if !ok || t.overflow {
		return streamEvidence{}, false
	}
	return *e, true
}

func (t *trace) RecordEvent(event qlogwriter.Event) {
	t.mu.Lock()
	defer t.mu.Unlock()
	switch e := event.(type) {
	case qlog.ParametersSet:
		if string(e.Initiator) == "remote" {
			t.remoteMax, t.remote09, t.peerSeen = uint64(e.MaxDatagramFrameSize), e.ResetStreamAt09, true
		} else if string(e.Initiator) == "local" {
			// The pinned native emitter, separately golden-tested, emits empty TP1d.
			// Only the REMOTE fact uses the exact-receipt overlay, not legacy support.
			t.localMax, t.local09, t.localSeen = uint64(e.MaxDatagramFrameSize), e.EnableResetStreamAt, true
		}
	case qlog.PacketSent:
		t.packet(true, e.Frames)
	case qlog.PacketReceived:
		t.packet(false, e.Frames)
	}
}

func (t *trace) packet(sent bool, frames []qlog.Frame) {
	for _, wrapper := range frames {
		switch f := wrapper.Frame.(type) {
		case *qlog.HandshakeDoneFrame:
			if !sent && !t.handshakeDoneReceived {
				t.handshakeDoneReceived = true
				close(t.handshakeDone)
			}
		case *qlog.StreamFrame:
			if sent {
				if e := t.get(uint64(f.StreamID)); e != nil {
					end := uint64(f.Offset + f.Length)
					if end > e.sentOffset {
						e.sentOffset = end
					}
				}
			}
		case *qlog.ResetStreamFrame:
			e := t.get(uint64(f.StreamID))
			if e == nil {
				continue
			}
			encoded, err := f.Append(nil, quic.Version1)
			if err != nil || len(encoded) == 0 {
				t.overflow = true
				continue
			}
			r := resetEvidence{uint64(f.FinalSize), uint64(f.ReliableSize),
				uint64(f.ErrorCode), uint64(encoded[0]), true}
			if sent {
				e.sent = r
			} else {
				e.received = r
			}
		case *qlog.StopSendingFrame:
			if !sent {
				if e := t.get(uint64(f.StreamID)); e != nil && !e.peerStop.present {
					e.peerStop = stopEvidence{code: uint64(f.ErrorCode), present: true}
				}
			}
		}
	}
}

func connectionTrace(ctx context.Context, _ bool, _ quic.ConnectionID) qlogwriter.Trace {
	return ctx.Value(traceKey{}).(*trace)
}
