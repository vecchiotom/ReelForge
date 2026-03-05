FROM golang:1.25-alpine AS build
WORKDIR /src

COPY go.mod go.sum ./
RUN go mod download

COPY . .

ARG VERSION=docker
RUN CGO_ENABLED=0 go build -mod=mod -ldflags="-s -w -X main.Version=${VERSION}" -o /app/reelforge .

FROM alpine:3.21
RUN apk add --no-cache wget

WORKDIR /app
COPY --from=build /app/reelforge .

EXPOSE 8080
ENTRYPOINT ["./reelforge"]
