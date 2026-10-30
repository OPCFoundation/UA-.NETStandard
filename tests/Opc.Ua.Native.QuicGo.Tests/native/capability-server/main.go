// Copyright (c) OPC Foundation. Licensed under the MIT License.
// Test-only independent peer with deliberately absent transport capabilities.
package main

import (
	"context"
	"crypto/tls"
	"crypto/x509"
	"encoding/json"
	"flag"
	"io"
	"net"
	"os"
	"time"

	quic "github.com/quic-go/quic-go"
)

func main() {
	datagrams := flag.Bool("datagrams", true, "advertise native datagrams")
	reset := flag.Bool("reset", true, "advertise native reliable reset")
	clientAuth := flag.Bool("client-auth", true, "request and verify the client certificate")
	confirm := flag.Bool("confirm", true, "deliver server short-header packets including HANDSHAKE_DONE")
	flag.Parse()
	var material struct {
		Root       []byte `json:"root"`
		Identities []struct {
			Cert []byte `json:"cert"`
			Key  []byte `json:"key"`
		} `json:"identities"`
	}
	if json.NewDecoder(io.LimitReader(os.Stdin, 262144)).Decode(&material) != nil || len(material.Identities) < 2 {
		os.Exit(2)
	}
	server := material.Identities[1]
	key, err := x509.ParsePKCS8PrivateKey(server.Key)
	clear(server.Key)
	if err != nil {
		os.Exit(3)
	}
	root, err := x509.ParseCertificate(material.Root)
	if err != nil {
		os.Exit(3)
	}
	trust := x509.NewCertPool()
	trust.AddCert(root)
	tlsConfig := &tls.Config{
		MinVersion: tls.VersionTLS13, MaxVersion: tls.VersionTLS13,
		NextProtos: []string{"h3"}, SessionTicketsDisabled: true,
		Certificates: []tls.Certificate{{Certificate: [][]byte{server.Cert}, PrivateKey: key}},
		ClientAuth:   tls.RequireAndVerifyClientCert, ClientCAs: trust,
	}
	if !*clientAuth {
		tlsConfig.ClientAuth = tls.NoClientCert
	}
	socket, err := net.ListenPacket("udp4", "127.0.0.1:0")
	if err != nil {
		os.Exit(4)
	}
	defer socket.Close()
	packet := &packetGate{PacketConn: socket, suppressShortHeader: !*confirm}
	transport := &quic.Transport{Conn: packet}
	defer transport.Close()
	listener, err := transport.Listen(tlsConfig, &quic.Config{
		EnableDatagrams: *datagrams, EnableStreamResetPartialDelivery: *reset,
		MaxIncomingStreams: 2, MaxIncomingUniStreams: 2,
		HandshakeIdleTimeout: time.Second, MaxIdleTimeout: 3 * time.Second,
	})
	if err != nil {
		os.Exit(4)
	}
	defer listener.Close()
	if json.NewEncoder(os.Stdout).Encode(listener.Addr().String()) != nil {
		os.Exit(5)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	conn, err := listener.Accept(ctx)
	if err == nil {
		state := conn.ConnectionState()
		if json.NewEncoder(os.Stdout).Encode(struct {
			PeerCertificates  int  `json:"peer_certificates"`
			VerifiedChains    int  `json:"verified_chains"`
			HandshakeComplete bool `json:"handshake_complete"`
		}{len(state.TLS.PeerCertificates), len(state.TLS.VerifiedChains), state.TLS.HandshakeComplete}) != nil {
			os.Exit(5)
		}
		defer conn.CloseWithError(0, "test peer finished")
		select {
		case <-ctx.Done():
		case <-conn.Context().Done():
		}
	}

}

// Packet loss injection only: QUIC/TLS encoding and processing remain native.
type packetGate struct {
	net.PacketConn
	suppressShortHeader bool
}

func (p *packetGate) WriteTo(data []byte, address net.Addr) (int, error) {
	if p.suppressShortHeader && len(data) > 0 && data[0]&0x80 == 0 {
		return len(data), nil
	}
	return p.PacketConn.WriteTo(data, address)
}
