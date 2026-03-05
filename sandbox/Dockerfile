FROM golang:1.25-alpine AS build
WORKDIR /src

COPY go.mod ./
RUN go mod download

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
