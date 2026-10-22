#!/bin/sh
set -eu

: "${VULTR_SSH_HOST:?VULTR_SSH_HOST is required}"
: "${VULTR_SSH_USER:?VULTR_SSH_USER is required}"
: "${VULTR_SSH_PORT:=22}"
: "${POSTGRES_TUNNEL_LOCAL_PORT:=15432}"
: "${POSTGRES_REMOTE_HOST:=127.0.0.1}"
: "${POSTGRES_REMOTE_PORT:=5432}"

source_key=/mnt/ssh-secrets/id_ed25519
source_known_hosts=/mnt/ssh-secrets/known_hosts
runtime_key=/run/tunnel/id_ed25519
runtime_known_hosts=/run/tunnel/known_hosts

if [ ! -s "$source_key" ] || [ ! -s "$source_known_hosts" ]; then
    echo "SSH key or pinned known_hosts entry is missing." >&2
    exit 1
fi

# Container Apps secret volumes are read-only and their mode is not suitable for OpenSSH keys.
install -m 0600 "$source_key" "$runtime_key"
install -m 0644 "$source_known_hosts" "$runtime_known_hosts"

export AUTOSSH_GATETIME=0
export AUTOSSH_POLL=30

exec autossh -M 0 -N -T \
    -L "127.0.0.1:${POSTGRES_TUNNEL_LOCAL_PORT}:${POSTGRES_REMOTE_HOST}:${POSTGRES_REMOTE_PORT}" \
    -p "$VULTR_SSH_PORT" \
    -i "$runtime_key" \
    -o BatchMode=yes \
    -o ExitOnForwardFailure=yes \
    -o ServerAliveInterval=30 \
    -o ServerAliveCountMax=3 \
    -o StrictHostKeyChecking=yes \
    -o "UserKnownHostsFile=$runtime_known_hosts" \
    "${VULTR_SSH_USER}@${VULTR_SSH_HOST}"
