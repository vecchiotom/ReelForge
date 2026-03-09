FROM golang:1.25-alpine AS build
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

FROM node:22-bookworm-slim

# Install Chrome dependencies + ffmpeg + docker-cli
RUN apt-get update && apt-get install -y --no-install-recommends \
	ca-certificates \
	curl \
	gnupg \
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

# Install Docker CLI
RUN install -m 0755 -d /etc/apt/keyrings \
	&& curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc \
	&& chmod a+r /etc/apt/keyrings/docker.asc \
	&& echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian bookworm stable" > /etc/apt/sources.list.d/docker.list \
	&& apt-get update \
	&& apt-get install -y --no-install-recommends docker-ce-cli \
	&& rm -rf /var/lib/apt/lists/*

WORKDIR /opt/remotion-template
COPY template/package.json ./package.json
RUN npm install --no-audit --no-fund
COPY template/ ./

WORKDIR /app
COPY --from=build /app/sandbox-executor ./

EXPOSE 8080
ENTRYPOINT ["/app/sandbox-executor"]
