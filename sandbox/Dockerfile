FROM golang:1.25-alpine AS gobuild
WORKDIR /src

RUN apk add --no-cache ca-certificates git

ARG GOPROXY=https://proxy.golang.org,direct
ARG GOSUMDB=sum.golang.org
ENV GOPROXY=${GOPROXY}
ENV GOSUMDB=${GOSUMDB}

COPY go.mod ./
RUN set -eux; \
		for i in 1 2 3 4 5; do \
			go mod download && break; \
			echo "go mod download failed (attempt ${i}), retrying..."; \
			sleep $((i * 3)); \
		done

COPY . .

ARG VERSION=docker
RUN CGO_ENABLED=0 go build -ldflags="-s -w -X main.Version=${VERSION}" -o /app/sandbox-executor .


# ─────────────────────────────────────────────────────────────────────────────
# sandbox-runtime — the image untrusted, model-authored code actually runs in.
#
# Everything in this stage is reachable by an attacker: `npm run build` executes
# whatever the caller wrote into package.json, so treat this filesystem as
# adversary-visible. It therefore contains only what Remotion needs to render:
# node, the Chrome shared libraries, ffmpeg, fonts, and the template.
#
# Deliberately NOT present here (all of which the control-plane stage adds on
# top): the Docker CLI, curl, wget, and gnupg. A Docker client inside the
# untrusted container is a standing invitation — the moment any daemon endpoint
# becomes reachable it is root on the host — and it buys the render path nothing.
# ─────────────────────────────────────────────────────────────────────────────
FROM node:22-bookworm-slim AS sandbox-runtime

RUN apt-get update && apt-get install -y --no-install-recommends \
	ca-certificates \
	ffmpeg \
	# Chrome dependencies
	libasound2 \
	libatk-bridge2.0-0 \
	libatk1.0-0 \
	libcups2 \
	libdbus-1-3 \
	libdrm2 \
	libgbm1 \
	libgtk-3-0 \
	libnspr4 \
	libnss3 \
	libx11-xcb1 \
	libxcomposite1 \
	libxdamage1 \
	libxfixes3 \
	libxkbcommon0 \
	libxrandr2 \
	xdg-utils \
	fonts-liberation \
	fonts-noto-color-emoji \
	&& rm -rf /var/lib/apt/lists/*

WORKDIR /opt/remotion-template
COPY template/package.json ./package.json
RUN npm install --no-audit --no-fund
COPY template/ ./

# Each sandbox container is started with `--entrypoint sh` and `--user node`, so
# no entrypoint is set here. Runs as node by default for anything that does not.
USER node


# ─────────────────────────────────────────────────────────────────────────────
# control-plane — the sandbox service itself (trusted).
#
# This is the image that holds the Docker socket, so it is the one thing in the
# system whose compromise is equivalent to root on the host. It builds on the
# runtime stage purely for layer reuse; the extra tooling below must never be
# allowed to leak back into the image untrusted code runs in.
# ─────────────────────────────────────────────────────────────────────────────
FROM sandbox-runtime AS control-plane

USER root

RUN apt-get update && apt-get install -y --no-install-recommends \
	curl \
	gnupg \
	&& install -m 0755 -d /etc/apt/keyrings \
	&& curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc \
	&& chmod a+r /etc/apt/keyrings/docker.asc \
	&& echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian bookworm stable" > /etc/apt/sources.list.d/docker.list \
	&& apt-get update \
	&& apt-get install -y --no-install-recommends docker-ce-cli \
	&& rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=gobuild /app/sandbox-executor ./

EXPOSE 8080
ENTRYPOINT ["/app/sandbox-executor"]
