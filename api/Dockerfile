FROM golang:1.25-alpine AS build
WORKDIR /src

RUN apk add --no-cache ca-certificates git

ARG GOPROXY=https://proxy.golang.org,direct
ARG GOSUMDB=sum.golang.org
ENV GOPROXY=${GOPROXY}
ENV GOSUMDB=${GOSUMDB}

COPY go.mod go.sum ./
RUN set -eux; \
		for i in 1 2 3 4 5; do \
			go mod download && break; \
			echo "go mod download failed (attempt ${i}), retrying..."; \
			sleep $((i * 3)); \
		done

COPY . .

ARG VERSION=docker
RUN CGO_ENABLED=0 go build -mod=mod -ldflags="-s -w -X main.Version=${VERSION}" -o /app/reelforge .

FROM alpine:3.21
RUN apk add --no-cache wget

WORKDIR /app
COPY --from=build /app/reelforge .

EXPOSE 8080
ENTRYPOINT ["./reelforge"]
