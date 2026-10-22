FROM alpine:3.21

RUN apk add --no-cache autossh openssh-client

COPY deployment/docker/postgres-tunnel-entrypoint.sh /usr/local/bin/postgres-tunnel-entrypoint
RUN chmod 0755 /usr/local/bin/postgres-tunnel-entrypoint \
    && addgroup -S tunnel \
    && adduser -S -D -H -G tunnel tunnel \
    && mkdir -p /run/tunnel \
    && chown tunnel:tunnel /run/tunnel

USER tunnel

ENTRYPOINT ["/usr/local/bin/postgres-tunnel-entrypoint"]
