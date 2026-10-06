// Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
// OPC Foundation MIT License 1.00 - http://opcfoundation.org/License/MIT/1.00/

// gopcua interop peer (client only): the same command line and stdout contract
// as tests/Opc.Ua.Interop.LegacyPeer, so the 2.0 interop tests can drive it.
//
//	client --url <u> --pki <dir> [--policy <uri>] [--mode <m>] [--user u --password p]
//	       [--checks a,b] [--expect-connect-error A,B] [--token-lifetime ms]
//	       [--token-test-seconds s] [--timeout-seconds s]
//
// The gopcua server cannot run the server contract (no Call, no
// TranslateBrowsePaths, user tokens are not checked), so "server" exits with
// a usage error.
package main

import (
	"bytes"
	"context"
	"crypto/rand"
	"crypto/rsa"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/json"
	"errors"
	"fmt"
	"math/big"
	"net"
	"net/url"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"time"

	"github.com/gopcua/opcua"
	"github.com/gopcua/opcua/id"
	"github.com/gopcua/opcua/ua"
)

const (
	referenceNamespace = "http://opcfoundation.org/Quickstarts/ReferenceServer"
	policyNone         = "http://opcfoundation.org/UA/SecurityPolicy#None"
	clientName         = "GopcuaInteropClient"
)

var options = map[string]string{}

func opt(name, def string) string {
	if v, ok := options[name]; ok {
		return v
	}
	return def
}

func main() {
	if len(os.Args) < 2 {
		fmt.Fprintln(os.Stderr, "error: missing command")
		os.Exit(2)
	}
	for i := 2; i < len(os.Args); i += 2 {
		if !strings.HasPrefix(os.Args[i], "--") || i+1 >= len(os.Args) {
			fmt.Fprintf(os.Stderr, "error: expected --name value, got %s\n", os.Args[i])
			os.Exit(2)
		}
		options[strings.ToLower(os.Args[i][2:])] = os.Args[i+1]
	}
	switch os.Args[1] {
	case "client":
		os.Exit(runClient())
	case "server":
		fmt.Fprintln(os.Stderr, "error: the gopcua peer is client only")
		os.Exit(2)
	default:
		fmt.Fprintf(os.Stderr, "error: unknown command %s\n", os.Args[1])
		os.Exit(2)
	}
}

// loadOrCreateIdentity keeps an RSA 2048 certificate in the .NET PKI layout:
// own/certs/<name>.der and own/private/<name>.der (PKCS#1).
func loadOrCreateIdentity(pki, applicationURI string) ([]byte, *rsa.PrivateKey, error) {
	certPath := filepath.Join(pki, "own", "certs", clientName+".der")
	keyPath := filepath.Join(pki, "own", "private", clientName+".der")
	if cert, err := os.ReadFile(certPath); err == nil {
		if keyDer, err := os.ReadFile(keyPath); err == nil {
			if key, err := x509.ParsePKCS1PrivateKey(keyDer); err == nil {
				return cert, key, nil
			}
		}
	}
	key, err := rsa.GenerateKey(rand.Reader, 2048)
	if err != nil {
		return nil, nil, err
	}
	uri, _ := url.Parse(applicationURI)
	serial, _ := rand.Int(rand.Reader, big.NewInt(1<<62))
	template := &x509.Certificate{
		SerialNumber:          serial,
		Subject:               pkix.Name{CommonName: clientName, Organization: []string{"OPC Foundation"}},
		NotBefore:             time.Now().Add(-time.Hour),
		NotAfter:              time.Now().AddDate(1, 0, 0),
		KeyUsage:              x509.KeyUsageDigitalSignature | x509.KeyUsageContentCommitment | x509.KeyUsageKeyEncipherment | x509.KeyUsageDataEncipherment | x509.KeyUsageCertSign,
		ExtKeyUsage:           []x509.ExtKeyUsage{x509.ExtKeyUsageClientAuth, x509.ExtKeyUsageServerAuth},
		BasicConstraintsValid: true,
		URIs:                  []*url.URL{uri},
		DNSNames:              []string{"localhost"},
		IPAddresses:           []net.IP{net.ParseIP("127.0.0.1")},
	}
	cert, err := x509.CreateCertificate(rand.Reader, template, template, &key.PublicKey, key)
	if err != nil {
		return nil, nil, err
	}
	_ = os.MkdirAll(filepath.Dir(certPath), 0o755)
	_ = os.MkdirAll(filepath.Dir(keyPath), 0o755)
	if err := os.WriteFile(certPath, cert, 0o644); err != nil {
		return nil, nil, err
	}
	if err := os.WriteFile(keyPath, x509.MarshalPKCS1PrivateKey(key), 0o600); err != nil {
		return nil, nil, err
	}
	return cert, key, nil
}

// ------------------------------------------------------------------ reporting

var passed, failed int

func report(check string, err error, started time.Time) {
	outcome, message := "Passed", ""
	if err != nil {
		outcome, message = "Failed", err.Error()
		failed++
	} else {
		passed++
	}
	line, _ := json.Marshal(map[string]any{"check": check, "outcome": outcome, "message": message,
		"milliseconds": time.Since(started).Milliseconds()})
	fmt.Println("RESULT " + string(line))
}

func statusName(err error) string {
	var sc ua.StatusCode
	if errors.As(err, &sc) {
		return sc.Error()
	}
	return err.Error()
}

// ------------------------------------------------------------------ checks

type ctx struct {
	c   *opcua.Client
	ns  uint16
	ep  *ua.EndpointDescription
	url string
	// connect opens another client on the same endpoint with the given identity.
	connect func(c context.Context, connect bool, authType ua.UserTokenType, auth ...opcua.Option) (*opcua.Client, error)
	// reconnect opens a client with the identity of this run.
	reconnect func(c context.Context) (*opcua.Client, error)
}

func (x *ctx) id(name string) *ua.NodeID { return ua.NewStringNodeID(x.ns, name) }

func valueID(n *ua.NodeID) *ua.ReadValueID {
	return &ua.ReadValueID{NodeID: n, AttributeID: ua.AttributeIDValue}
}

func (x *ctx) read(c context.Context, ids ...*ua.ReadValueID) ([]*ua.DataValue, error) {
	resp, err := x.c.Read(c, &ua.ReadRequest{NodesToRead: ids, TimestampsToReturn: ua.TimestampsToReturnBoth})
	if err != nil {
		return nil, err
	}
	if len(resp.Results) != len(ids) {
		return nil, fmt.Errorf("result count %d", len(resp.Results))
	}
	return resp.Results, nil
}

func (x *ctx) write(c context.Context, n *ua.NodeID, v *ua.Variant) (ua.StatusCode, error) {
	resp, err := x.c.Write(c, &ua.WriteRequest{NodesToWrite: []*ua.WriteValue{{
		NodeID: n, AttributeID: ua.AttributeIDValue,
		Value: &ua.DataValue{EncodingMask: ua.DataValueValue, Value: v},
	}}})
	if err != nil {
		return ua.StatusBad, err
	}
	return resp.Results[0], nil
}

func browseAll(n *ua.NodeID) *ua.BrowseDescription {
	return &ua.BrowseDescription{
		NodeID: n, BrowseDirection: ua.BrowseDirectionForward,
		ReferenceTypeID: ua.NewNumericNodeID(0, id.HierarchicalReferences), IncludeSubtypes: true,
		ResultMask: uint32(ua.BrowseResultMaskAll),
	}
}

func (x *ctx) browse(c context.Context, n *ua.NodeID, maxRefs uint32) ([]*ua.ReferenceDescription, error) {
	resp, err := x.c.Browse(c, &ua.BrowseRequest{View: &ua.ViewDescription{ViewID: ua.NewTwoByteNodeID(0)},
		RequestedMaxReferencesPerNode: maxRefs, NodesToBrowse: []*ua.BrowseDescription{browseAll(n)}})
	if err != nil {
		return nil, err
	}
	r := resp.Results[0]
	if r.StatusCode != ua.StatusOK {
		return nil, fmt.Errorf("browse of %s returned %s", n, r.StatusCode.Error())
	}
	refs := append([]*ua.ReferenceDescription{}, r.References...)
	cp := r.ContinuationPoint
	for len(cp) > 0 {
		next, err := x.c.BrowseNext(c, &ua.BrowseNextRequest{ContinuationPoints: [][]byte{cp}})
		if err != nil {
			return nil, err
		}
		r = next.Results[0]
		if r.StatusCode != ua.StatusOK {
			return nil, fmt.Errorf("browse next of %s returned %s", n, r.StatusCode.Error())
		}
		refs = append(refs, r.References...)
		cp = r.ContinuationPoint
	}
	return refs, nil
}

func checkNamespaceArray(c context.Context, x *ctx) error {
	if x.ns == 0 {
		return errors.New("reference server namespace is missing")
	}
	return nil
}

func checkServerStatus(c context.Context, x *ctx) error {
	values, err := x.read(c, valueID(ua.NewNumericNodeID(0, id.Server_ServerStatus)))
	if err != nil {
		return err
	}
	if values[0].Status != ua.StatusOK {
		return fmt.Errorf("status %s", values[0].Status.Error())
	}
	xo, ok := values[0].Value.Value().(*ua.ExtensionObject)
	if !ok {
		return fmt.Errorf("ServerStatus is %T", values[0].Value.Value())
	}
	status, ok := xo.Value.(*ua.ServerStatusDataType)
	if !ok {
		return fmt.Errorf("ServerStatus did not decode to ServerStatusDataType: %T", xo.Value)
	}
	if status.State != ua.ServerStateRunning {
		return fmt.Errorf("server state %d", status.State)
	}
	if status.BuildInfo == nil || status.BuildInfo.ProductURI == "" {
		return errors.New("BuildInfo is empty")
	}
	return nil
}

func checkBrowseObjects(c context.Context, x *ctx) error {
	refs, err := x.browse(c, ua.NewNumericNodeID(0, id.ObjectsFolder), 0)
	if err != nil {
		return err
	}
	names := map[string]bool{}
	for _, r := range refs {
		names[r.BrowseName.Name] = true
	}
	if !names["Server"] {
		return errors.New("Server object not found")
	}
	if !names["CTT"] {
		return errors.New("CTT folder not found")
	}
	return nil
}

func checkTranslate(c context.Context, x *ctx) error {
	elements := []*ua.RelativePathElement{}
	for _, n := range []string{"Server", "ServerStatus", "CurrentTime"} {
		elements = append(elements, &ua.RelativePathElement{
			ReferenceTypeID: ua.NewNumericNodeID(0, id.HierarchicalReferences), IncludeSubtypes: true,
			TargetName: &ua.QualifiedName{NamespaceIndex: 0, Name: n},
		})
	}
	req := &ua.TranslateBrowsePathsToNodeIDsRequest{BrowsePaths: []*ua.BrowsePath{{
		StartingNode: ua.NewNumericNodeID(0, id.ObjectsFolder), RelativePath: &ua.RelativePath{Elements: elements},
	}}}
	var resp *ua.TranslateBrowsePathsToNodeIDsResponse
	err := x.c.Send(c, req, func(r ua.Response) error {
		var ok bool
		if resp, ok = r.(*ua.TranslateBrowsePathsToNodeIDsResponse); !ok {
			return fmt.Errorf("unexpected response %T", r)
		}
		return nil
	})
	if err != nil {
		return err
	}
	res := resp.Results[0]
	if res.StatusCode != ua.StatusOK || len(res.Targets) == 0 {
		return fmt.Errorf("status %s", res.StatusCode.Error())
	}
	if got := res.Targets[0].TargetID.NodeID.String(); got != ua.NewNumericNodeID(0, id.Server_ServerStatus_CurrentTime).String() {
		return fmt.Errorf("unexpected target %s", got)
	}
	return nil
}

func checkReadScalars(c context.Context, x *ctx) error {
	names := []string{"Boolean", "Int32", "Double", "String", "DateTime", "Guid", "ByteString", "LocalizedText",
		"QualifiedName", "NodeId", "Arrays_Int32", "Arrays_String"}
	ids := []*ua.ReadValueID{}
	for _, n := range names {
		ids = append(ids, valueID(x.id("Scalar_Static_"+n)))
	}
	values, err := x.read(c, ids...)
	if err != nil {
		return err
	}
	for i, v := range values {
		if v.Status != ua.StatusOK {
			return fmt.Errorf("%s: %s", names[i], v.Status.Error())
		}
	}
	return nil
}

func checkReadAttributes(c context.Context, x *ctx) error {
	n := x.id("Scalar_Static_Int32")
	values, err := x.read(c, &ua.ReadValueID{NodeID: n, AttributeID: ua.AttributeIDNodeClass},
		&ua.ReadValueID{NodeID: n, AttributeID: ua.AttributeIDBrowseName},
		&ua.ReadValueID{NodeID: n, AttributeID: ua.AttributeIDDataType})
	if err != nil {
		return err
	}
	if v, _ := values[0].Value.Value().(int32); v != int32(ua.NodeClassVariable) {
		return fmt.Errorf("NodeClass %v", values[0].Value.Value())
	}
	if q, _ := values[1].Value.Value().(*ua.QualifiedName); q == nil || q.Name != "Scalar_Static_Int32" {
		return fmt.Errorf("BrowseName %v", values[1].Value.Value())
	}
	if d, _ := values[2].Value.Value().(*ua.NodeID); d == nil || d.String() != ua.NewNumericNodeID(0, id.Int32).String() {
		return fmt.Errorf("DataType %v", values[2].Value.Value())
	}
	return nil
}

func checkWriteReadBack(c context.Context, x *ctx) error {
	text := "written by gopcua äöü"
	doubles := []float64{1.5, -2.25, 1e300}
	for _, w := range []struct {
		name  string
		value any
	}{{"Scalar_Static_Int32", int32(1234567)}, {"Scalar_Static_String", text}, {"Scalar_Static_Arrays_Double", doubles}} {
		status, err := x.write(c, x.id(w.name), ua.MustVariant(w.value))
		if err != nil {
			return err
		}
		if status != ua.StatusOK {
			return fmt.Errorf("%s write returned %s", w.name, status.Error())
		}
		values, err := x.read(c, valueID(x.id(w.name)))
		if err != nil {
			return err
		}
		if fmt.Sprint(values[0].Value.Value()) != fmt.Sprint(w.value) {
			return fmt.Errorf("%s read back %v", w.name, values[0].Value.Value())
		}
	}
	return nil
}

func checkCallMethods(c context.Context, x *ctx) error {
	hello, err := x.c.Call(c, &ua.CallMethodRequest{ObjectID: x.id("Methods"), MethodID: x.id("Methods_Hello"),
		InputArguments: []*ua.Variant{ua.MustVariant("gopcua")}})
	if err != nil {
		return err
	}
	if hello.StatusCode != ua.StatusOK || len(hello.OutputArguments) != 1 || hello.OutputArguments[0].Value() != "hello gopcua" {
		return fmt.Errorf("Hello returned %s %v", hello.StatusCode.Error(), hello.OutputArguments)
	}
	add, err := x.c.Call(c, &ua.CallMethodRequest{ObjectID: x.id("Methods"), MethodID: x.id("Methods_Add"),
		InputArguments: []*ua.Variant{ua.MustVariant(float32(1.5)), ua.MustVariant(uint32(2))}})
	if err != nil {
		return err
	}
	if add.StatusCode != ua.StatusOK || len(add.OutputArguments) != 1 || add.OutputArguments[0].Value() != float32(3.5) {
		return fmt.Errorf("Add returned %s %v", add.StatusCode.Error(), add.OutputArguments)
	}
	return nil
}

func checkSubscription(c context.Context, x *ctx) error {
	ch := make(chan *opcua.PublishNotificationData, 32)
	sub, err := x.c.Subscribe(c, &opcua.SubscriptionParameters{Interval: 100 * time.Millisecond,
		LifetimeCount: 100, MaxKeepAliveCount: 10}, ch)
	if err != nil {
		return err
	}
	defer sub.Cancel(context.Background())
	item := opcua.NewMonitoredItemCreateRequestWithDefaults(ua.NewNumericNodeID(0, id.Server_ServerStatus_CurrentTime),
		ua.AttributeIDValue, 1)
	item.RequestedParameters.SamplingInterval = 100
	item.RequestedParameters.QueueSize = 10
	res, err := sub.Monitor(c, ua.TimestampsToReturnBoth, item)
	if err != nil {
		return err
	}
	if res.Results[0].StatusCode != ua.StatusOK {
		return fmt.Errorf("monitored item %s", res.Results[0].StatusCode.Error())
	}
	count := 0
	timeout := time.After(15 * time.Second)
	for count < 3 {
		select {
		case n := <-ch:
			if dc, ok := n.Value.(*ua.DataChangeNotification); ok {
				count += len(dc.MonitoredItems)
			}
		case <-timeout:
			return fmt.Errorf("only %d data change notifications in 15 s", count)
		}
	}
	return nil
}

func checkComplexTypes(c context.Context, x *ctx) error {
	return errors.New("gopcua has no DataTypeDefinition based decoding of custom structures")
}

func writeAndCompare(c context.Context, x *ctx, n *ua.NodeID, value any) error {
	status, err := x.write(c, n, ua.MustVariant(value))
	if err != nil {
		return err
	}
	if status != ua.StatusOK {
		return fmt.Errorf("write of %s returned %s", n, status.Error())
	}
	values, err := x.read(c, valueID(n))
	if err != nil {
		return err
	}
	equal := false
	switch v := value.(type) {
	case []byte:
		got, _ := values[0].Value.Value().([]byte)
		equal = bytes.Equal(got, v)
	case []int32:
		got, _ := values[0].Value.Value().([]int32)
		equal = len(got) == len(v)
		for i := 0; equal && i < len(v); i++ {
			equal = got[i] == v[i]
		}
	}
	if !equal {
		return fmt.Errorf("%s read back a different value", n)
	}
	return nil
}

func checkLargeArray(c context.Context, x *ctx) error {
	values := make([]int32, 200000)
	for i := range values {
		values[i] = int32((i * 7919) ^ 0x5A5A)
	}
	return writeAndCompare(c, x, x.id("Scalar_Static_Arrays_Int32"), values)
}

func checkLargeByteString(c context.Context, x *ctx) error {
	value := make([]byte, 3*1024*1024)
	seed := uint32(4711)
	for i := range value {
		seed = seed*1103515245 + 12345
		value[i] = byte(seed >> 24)
	}
	return writeAndCompare(c, x, x.id("Scalar_Static_ByteString"), value)
}

func checkOversized(c context.Context, x *ctx) error {
	status, err := x.write(c, x.id("Scalar_Static_ByteString"), ua.MustVariant(make([]byte, 5*1024*1024)))
	result := status.Error()
	if err != nil {
		result = statusName(err)
	} else if status == ua.StatusOK {
		return errors.New("a 5 MB ByteString write was accepted")
	}
	values, err := x.read(c, valueID(ua.NewNumericNodeID(0, id.Server_ServerStatus_State)))
	if err != nil || values[0].Status != ua.StatusOK {
		return fmt.Errorf("the session is unusable after the rejected write (%s): %v", result, err)
	}
	fmt.Println("INFO OversizedRequestRejected: " + result)
	return nil
}

func checkBrowseContinuation(c context.Context, x *ctx) error {
	folder := x.id("Scalar_Static")
	all, err := x.browse(c, folder, 0)
	if err != nil {
		return err
	}
	paged, err := x.browse(c, folder, 3)
	if err != nil {
		return err
	}
	if len(all) <= 10 || len(paged) != len(all) {
		return fmt.Errorf("paged browse returned %d of %d references", len(paged), len(all))
	}
	for i := range all {
		if all[i].NodeID.String() != paged[i].NodeID.String() {
			return errors.New("paged browse returned different references")
		}
	}
	first, err := x.c.Browse(c, &ua.BrowseRequest{View: &ua.ViewDescription{ViewID: ua.NewTwoByteNodeID(0)},
		RequestedMaxReferencesPerNode: 3, NodesToBrowse: []*ua.BrowseDescription{browseAll(folder)}})
	if err != nil {
		return err
	}
	cp := first.Results[0].ContinuationPoint
	if len(cp) == 0 {
		return errors.New("no continuation point returned")
	}
	released, err := x.c.BrowseNext(c, &ua.BrowseNextRequest{ReleaseContinuationPoints: true, ContinuationPoints: [][]byte{cp}})
	if err != nil {
		return err
	}
	if len(released.Results) > 0 && released.Results[0].StatusCode != ua.StatusOK {
		return fmt.Errorf("releasing the continuation point returned %s", released.Results[0].StatusCode.Error())
	}
	reused, err := x.c.BrowseNext(c, &ua.BrowseNextRequest{ContinuationPoints: [][]byte{cp}})
	if err != nil {
		return err
	}
	if reused.Results[0].StatusCode != ua.StatusBadContinuationPointInvalid {
		return fmt.Errorf("a released continuation point returned %s", reused.Results[0].StatusCode.Error())
	}
	return nil
}

func checkReadManyNodes(c context.Context, x *ctx) error {
	values, err := x.read(c, valueID(ua.NewNumericNodeID(0, id.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead)))
	if err != nil {
		return err
	}
	limit, _ := values[0].Value.Value().(uint32)
	if limit == 0 {
		return errors.New("the server published no MaxNodesPerRead")
	}
	ids := []*ua.ReadValueID{}
	for i := 0; i < int(limit)*2+1; i++ {
		ids = append(ids, valueID(ua.NewNumericNodeID(0, id.Server_ServerStatus_State)))
	}
	good := 0
	for offset := 0; offset < len(ids); offset += int(limit) {
		end := min(offset+int(limit), len(ids))
		resp, err := x.c.Read(c, &ua.ReadRequest{NodesToRead: ids[offset:end]})
		if err != nil {
			return err
		}
		for _, r := range resp.Results {
			if r.Status == ua.StatusOK {
				good++
			}
		}
	}
	if good != len(ids) {
		return fmt.Errorf("%d of %d reads split by MaxNodesPerRead %d succeeded", good, len(ids), limit)
	}
	_, err = x.c.Read(c, &ua.ReadRequest{NodesToRead: ids})
	if err == nil || !errors.Is(err, ua.StatusBadTooManyOperations) {
		return fmt.Errorf("reading %d nodes in one request with MaxNodesPerRead %d returned %v", len(ids), limit, err)
	}
	return nil
}

func checkTokenRenewal(c context.Context, x *ctx) error {
	seconds, _ := strconv.Atoi(opt("token-test-seconds", "65"))
	end := time.Now().Add(time.Duration(seconds) * time.Second)
	for reads := 0; time.Now().Before(end); reads++ {
		values, err := x.read(c, valueID(ua.NewNumericNodeID(0, id.Server_ServerStatus_CurrentTime)))
		if err != nil {
			return fmt.Errorf("read %d failed: %w", reads, err)
		}
		if values[0].Status != ua.StatusOK {
			return fmt.Errorf("read %d returned %s", reads, values[0].Status.Error())
		}
		time.Sleep(500 * time.Millisecond)
	}
	return nil
}

var checks = []struct {
	name string
	fn   func(context.Context, *ctx) error
}{
	{"NamespaceArray", checkNamespaceArray}, {"ReadServerStatusStructure", checkServerStatus},
	{"BrowseObjectsFolder", checkBrowseObjects}, {"TranslateBrowsePath", checkTranslate},
	{"ReadScalars", checkReadScalars}, {"ReadAttributes", checkReadAttributes},
	{"WriteAndReadBack", checkWriteReadBack}, {"CallMethods", checkCallMethods},
	{"Subscription", checkSubscription}, {"ComplexTypes", checkComplexTypes},
	{"LargeArrayRoundTrip", checkLargeArray}, {"LargeByteStringRoundTrip", checkLargeByteString},
	{"OversizedRequestRejected", checkOversized}, {"BrowseContinuationPoints", checkBrowseContinuation},
	{"ReadManyNodes", checkReadManyNodes}, {"TokenRenewal", checkTokenRenewal},
	// Feature checks (features.go); SessionReconnect last.
	{"EventSubscription", checkEventSubscription}, {"ConditionRefresh", checkConditionRefresh},
	{"AlarmAcknowledge", checkAlarmAcknowledge}, {"DeadbandFilter", checkDeadbandFilter},
	{"QueueOverflow", checkQueueOverflow}, {"Triggering", checkTriggering},
	{"Republish", checkRepublish}, {"TransferSubscription", checkTransferSubscription},
	{"WrongPasswordRejected", checkWrongPassword}, {"X509UserToken", checkX509UserToken},
	{"RegisterNodes", checkRegisterNodes}, {"HistoryReadRaw", checkHistoryReadRaw},
	{"NodeManagement", checkNodeManagement}, {"IndexRange", checkIndexRange},
	{"FindServers", checkFindServers}, {"SessionReconnect", checkSessionReconnect},
}

func runClient() int {
	endpointURL, pki := opt("url", ""), opt("pki", "")
	if endpointURL == "" || pki == "" {
		fmt.Fprintln(os.Stderr, "error: client needs --url and --pki")
		return 2
	}
	// Mirrors the 2.0 reference server fixture (1M elements) instead of the
	// 64K default, so the large-message checks are bounded by the server.
	ua.MaxVariantArrayLength = 1024 * 1024
	applicationURI := "urn:localhost:opcfoundation.org:" + clientName
	cert, key, err := loadOrCreateIdentity(pki, applicationURI)
	if err != nil {
		fmt.Fprintln(os.Stderr, "FATAL creating the certificate failed: "+err.Error())
		return 3
	}
	if opt("init-only", "") == "true" {
		fmt.Println("PEER-PKI-READY " + applicationURI)
		return 0
	}
	selected := map[string]bool{}
	defaults := "NamespaceArray,ReadServerStatusStructure,BrowseObjectsFolder,TranslateBrowsePath,ReadScalars,ReadAttributes,WriteAndReadBack,CallMethods,Subscription"
	for _, s := range strings.Split(opt("checks", defaults), ",") {
		selected[s] = true
	}
	expected := []string{}
	if e := opt("expect-connect-error", ""); e != "" {
		expected = strings.Split(e, ",")
	}
	timeoutSeconds, _ := strconv.Atoi(opt("timeout-seconds", "300"))
	deadline, cancel := context.WithTimeout(context.Background(), time.Duration(timeoutSeconds)*time.Second)
	defer cancel()
	tokenSeconds, _ := strconv.Atoi(opt("token-test-seconds", "65"))
	checkTimeout := time.Duration(tokenSeconds+45) * time.Second

	started := time.Now()
	policy := opt("policy", policyNone)
	mode := ua.MessageSecurityModeFromString(opt("mode", "None"))
	endpoints, err := opcua.GetEndpoints(deadline, endpointURL)
	var client *opcua.Client
	var ep *ua.EndpointDescription
	// newClient creates a client with this run's channel settings; connect
	// false only creates it (for Dial + ActivateSession of a detached session).
	newClient := func(c context.Context, connect bool, authType ua.UserTokenType, auth ...opcua.Option) (*opcua.Client, error) {
		opts := []opcua.Option{
			opcua.ApplicationURI(applicationURI), opcua.Certificate(cert), opcua.PrivateKey(key),
			opcua.SecurityFromEndpoint(ep, authType)}
		opts = append(opts, auth...)
		opts = append(opts, opcua.AutoReconnect(false),
			opcua.RequestTimeout(60*time.Second), opcua.SessionTimeout(120*time.Second),
			// Mirrors the 2.0 reference server fixture: the large-message checks
			// are bounded by the server, not by this client.
			opcua.MaxMessageSize(16*1024*1024), opcua.MaxChunkCount(0))
		if ms, _ := strconv.Atoi(opt("token-lifetime", "0")); ms > 0 {
			opts = append(opts, opcua.Lifetime(time.Duration(ms)*time.Millisecond))
		}
		cl, err := opcua.NewClient(ep.EndpointURL, opts...)
		if err != nil || !connect {
			return cl, err
		}
		if err := cl.Connect(c); err != nil {
			return nil, err
		}
		return cl, nil
	}
	authType := ua.UserTokenTypeAnonymous
	auth := opcua.AuthAnonymous()
	if user := opt("user", ""); user != "" {
		authType = ua.UserTokenTypeUserName
		auth = opcua.AuthUsername(user, opt("password", ""))
	}
	if err == nil {
		ep, err = opcua.SelectEndpoint(endpoints, policy, mode)
		if err == nil {
			client, err = newClient(deadline, true, authType, auth)
		}
	}
	connected := err == nil
	switch {
	case len(expected) > 0 && connected:
		report("Connect", fmt.Errorf("expected %v, but the connect succeeded", expected), started)
	case len(expected) > 0:
		name := statusName(err)
		match := false
		for _, e := range expected {
			match = match || strings.Contains(name, e)
		}
		if match {
			report("Connect", nil, started)
		} else {
			report("Connect", fmt.Errorf("expected %v, the connect failed with %s", expected, name), started)
		}
	case !connected:
		report("Connect", fmt.Errorf("%s connecting to %s with %s/%s", statusName(err), endpointURL, policy, opt("mode", "None")), started)
	default:
		report("Connect", nil, started)
	}

	if connected && len(expected) == 0 {
		x := &ctx{c: client, ep: ep, url: endpointURL}
		x.connect = func(c context.Context, connect bool, t ua.UserTokenType, a ...opcua.Option) (*opcua.Client, error) {
			return newClient(c, connect, t, a...)
		}
		x.reconnect = func(c context.Context) (*opcua.Client, error) { return newClient(c, true, authType, auth) }
		if ns, err := client.FindNamespace(deadline, referenceNamespace); err == nil {
			x.ns = ns
		}
		// A check that times out keeps its context; the next checks continue
		// on a fresh copy of this template with a new client.
		template := *x
		template.c = nil
		for _, check := range checks {
			if !selected[check.name] {
				continue
			}
			t := time.Now()
			// Each check runs with its own time limit, so a gopcua call that
			// never returns (some ignore their context, e.g. Subscription.Cancel
			// while the publish loop is paused) fails that check instead of
			// stalling the remaining ones past the harness timeout.
			checkCtx, checkCancel := context.WithTimeout(deadline, checkTimeout)
			done := make(chan error, 1)
			go func(cx *ctx) {
				defer func() {
					if p := recover(); p != nil {
						done <- fmt.Errorf("panic: %v", p)
					}
				}()
				done <- check.fn(checkCtx, cx)
			}(x)
			var err error
			select {
			case err = <-done:
			case <-time.After(checkTimeout + 5*time.Second):
				err = fmt.Errorf("the check did not finish within %s", checkTimeout)
				// The abandoned goroutine keeps the old context and client;
				// nothing after this point touches either.
				fresh := template
				x = &fresh
			}
			checkCancel()
			report(check.name, err, t)
			if err != nil {
				x.ensureConnected(deadline)
			}
		}
		t := time.Now()
		if x.c == nil {
			report("CloseSession", errors.New("no session left to close"), t)
		} else {
			closeCtx, closeCancel := context.WithTimeout(context.Background(), 30*time.Second)
			report("CloseSession", x.c.Close(closeCtx), t)
			closeCancel()
		}
	} else if client != nil {
		_ = client.Close(context.Background())
	}
	fmt.Printf("SUMMARY passed=%d failed=%d\n", passed, failed)
	if failed > 0 {
		return 1
	}
	return 0
}
