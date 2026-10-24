// Copyright (c) OPC Foundation. Licensed under the MIT License.
package main

import (
	"bytes"
	"crypto"
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rsa"
	"crypto/sha256"
	"crypto/tls"
	"crypto/x509"
	"errors"
)

type peerRule struct {
	uri  string
	spki [32]byte
}

type credentials struct {
	certificate, issuers, key, roots []byte
	localURI, serverName             string
	peers                            []peerRule
}

type configuration struct {
	address, alpn        string
	identity             credentials
	streamLimit          uint32
	idleTimeout          uint32
	authenticationPolicy uint32
}

func certificateBundle(data []byte, optional bool) ([]*x509.Certificate, error) {
	if len(data) == 0 && optional {
		return nil, nil
	}
	certs, err := x509.ParseCertificates(data)
	if err != nil || len(certs) == 0 || len(certs) > 16 {
		return nil, errors.New("explicit bounded DER certificate bundle required")
	}
	return certs, nil
}

func applicationMatches(cert *x509.Certificate, uri string) bool {
	for _, san := range cert.URIs {
		if san.String() == uri {
			return true
		}
	}
	return false
}

func verifiedPeer(state tls.ConnectionState) (*x509.Certificate, error) {
	if state.Version != tls.VersionTLS13 ||
		len(state.VerifiedChains) == 0 || len(state.VerifiedChains[0]) == 0 ||
		len(state.PeerCertificates) == 0 {
		return nil, errors.New("verified TLS1.3 peer identity required")
	}
	leaf := state.VerifiedChains[0][0]
	if len(state.VerifiedChains[0]) > 16 || len(state.PeerCertificates) > 16 {
		return nil, errors.New("peer certificate chain exceeds the credential bound")
	}
	for _, cert := range state.VerifiedChains[0] {
		if len(cert.Raw) > 16384 {
			return nil, errors.New("peer certificate exceeds the credential bound")
		}
	}
	if !bytes.Equal(leaf.Raw, state.PeerCertificates[0].Raw) {
		return nil, errors.New("verified chain leaf mismatch")
	}
	return leaf, nil
}

func authorizedPeer(state tls.ConnectionState, peers []peerRule) (string, error) {
	leaf, err := verifiedPeer(state)
	if err != nil {
		return "", err
	}
	key := sha256.Sum256(leaf.RawSubjectPublicKeyInfo)
	for _, peer := range peers {
		if peer.spki == key && applicationMatches(leaf, peer.uri) {
			return peer.uri, nil
		}
	}
	return "", errors.New("peer application URI and key are not authorized")
}

func makeTLS(c *configuration, client bool) (*tls.Config, error) {
	m := &c.identity
	defer clear(m.key)
	transportOnly := c.authenticationPolicy == 1
	roots, err := certificateBundle(m.roots, transportOnly && !client)
	if err != nil {
		return nil, err
	}
	trust := x509.NewCertPool()
	for _, root := range roots {
		trust.AddCert(root)
	}
	config := &tls.Config{
		MinVersion: tls.VersionTLS13, MaxVersion: tls.VersionTLS13,
		NextProtos: []string{c.alpn}, SessionTicketsDisabled: true,
	}
	if transportOnly && client {
		config.RootCAs, config.ServerName = trust, m.serverName
		config.VerifyConnection = func(state tls.ConnectionState) error {
			_, err := verifiedPeer(state)
			return err
		}
		return config, nil
	}
	leaf, err := x509.ParseCertificate(m.certificate)
	if err != nil || (!transportOnly && !applicationMatches(leaf, m.localURI)) {
		return nil, errors.New("invalid local application certificate")
	}
	key, err := x509.ParsePKCS8PrivateKey(m.key)
	if err != nil {
		return nil, errors.New("exportable unencrypted PKCS8 identity required")
	}
	var signer crypto.Signer
	switch k := key.(type) {
	case *rsa.PrivateKey:
		if k.N.BitLen() < 2048 || k.N.BitLen() > 4096 || len(k.Primes) != 2 || k.Validate() != nil {
			return nil, errors.New("invalid two-prime RSA2048..4096 identity")
		}
		signer = k
	case *ecdsa.PrivateKey:
		if k.Curve != elliptic.P256() && k.Curve != elliptic.P384() && k.Curve != elliptic.P521() {
			return nil, errors.New("unsupported ECDSA identity curve")
		}
		signer = k
	default:
		return nil, errors.New("unsupported identity signing capability")
	}
	public, err := x509.MarshalPKIXPublicKey(signer.Public())
	if err != nil || !bytes.Equal(public, leaf.RawSubjectPublicKeyInfo) {
		return nil, errors.New("identity certificate/key mismatch")
	}
	issuers, err := certificateBundle(m.issuers, true)
	if err != nil {
		return nil, err
	}
	cert := tls.Certificate{Certificate: [][]byte{leaf.Raw}, PrivateKey: signer, Leaf: leaf}
	for _, issuer := range issuers {
		cert.Certificate = append(cert.Certificate, issuer.Raw)
	}
	config.Certificates = []tls.Certificate{cert}
	if !transportOnly {
		config.VerifyConnection = func(state tls.ConnectionState) error {
			_, err := authorizedPeer(state, m.peers)
			return err
		}
	}
	if client {
		config.RootCAs, config.ServerName = trust, m.serverName
		config.GetClientCertificate = func(request *tls.CertificateRequestInfo) (*tls.Certificate, error) {
			observation, ok := request.Context().Value(traceKey{}).(*trace)
			if !ok {
				return nil, errors.New("missing connection-bound credential observation")
			}
			observation.clientCertificateRequested.Store(true)
			if err := request.SupportsCertificate(&cert); err != nil {
				return nil, err
			}
			observation.localCertificateSelected.Store(true)
			return &cert, nil
		}
	} else {
		config.ClientAuth, config.ClientCAs = tls.RequireAndVerifyClientCert, trust
		if transportOnly {
			config.ClientAuth, config.ClientCAs = tls.NoClientCert, nil
		}
		// Force a per-connection selection callback even when SNI is absent.
		config.Certificates = nil
		config.GetCertificate = func(hello *tls.ClientHelloInfo) (*tls.Certificate, error) {
			observation, ok := hello.Context().Value(traceKey{}).(*trace)
			if !ok {
				return nil, errors.New("missing connection-bound credential observation")
			}
			if err := hello.SupportsCertificate(&cert); err != nil {
				return nil, err
			}
			observation.localCertificateSelected.Store(true)
			return &cert, nil
		}
	}
	return config, nil
}
