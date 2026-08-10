{{/*
Chart name and fullname, per the standard Helm conventions.
*/}}
{{- define "ai-model-management.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{- define "ai-model-management.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{- define "ai-model-management.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{- define "ai-model-management.labels" -}}
helm.sh/chart: {{ include "ai-model-management.chart" . }}
{{ include "ai-model-management.selectorLabels" . }}
{{- if .Chart.AppVersion }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{- define "ai-model-management.selectorLabels" -}}
app.kubernetes.io/name: {{ include "ai-model-management.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{- define "ai-model-management.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "ai-model-management.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{/*
The Secret holding the credential, whichever way it was supplied.

Returns an empty string when there is none, which is what the anonymous and
workload-identity paths want: neither stores a secret at all, so mounting an
empty volume for them would only invite someone to fill it.
*/}}
{{- define "ai-model-management.credentialSecretName" -}}
{{- if .Values.credentials.existingSecret }}
{{- .Values.credentials.existingSecret }}
{{- else if .Values.credentials.create }}
{{- printf "%s-credentials" (include "ai-model-management.fullname" .) }}
{{- end }}
{{- end }}

{{/*
Whether a credential is mounted at all.
*/}}
{{- define "ai-model-management.hasCredential" -}}
{{- if include "ai-model-management.credentialSecretName" . -}}
true
{{- end }}
{{- end }}
