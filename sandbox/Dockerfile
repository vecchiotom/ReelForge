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

FROM node:22-alpine
RUN apk add --no-cache docker-cli chromium ffmpeg nss freetype harfbuzz ttf-freefont

ENV PUPPETEER_EXECUTABLE_PATH=/usr/bin/chromium

WORKDIR /opt/remotion-template
COPY template/package.json ./package.json
RUN npm install --no-audit --no-fund
COPY template/ ./

WORKDIR /app
COPY --from=build /app/sandbox-executor ./

EXPOSE 8080
ENTRYPOINT ["./sandbox-executor"]
