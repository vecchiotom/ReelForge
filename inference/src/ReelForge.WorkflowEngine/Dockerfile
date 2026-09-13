FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS base
USER root
# ffmpeg/ffprobe run in-process for the video-editing pipeline (plan §2) — never in the sandbox
# service, which is untrusted-code territory this feature deliberately does not touch. Both the
# video scratch directory and the shared Data Protection key ring (/keys — encrypts/decrypts
# inference-provider API keys, mounted here and in the Inference API) are created and chowned to
# $APP_UID here, in the image, because Docker seeds a fresh named volume's ownership from the
# image directory at first mount (R12); creating them only at container runtime would leave the
# volumes root-owned and unwritable by the non-root user.
RUN apk add --no-cache ffmpeg font-dejavu \
 && mkdir -p /var/tmp/reelforge-video /keys \
 && chown -R $APP_UID:$APP_UID /var/tmp/reelforge-video /keys
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/ReelForge.Shared/ReelForge.Shared.csproj", "src/ReelForge.Shared/"]
COPY ["src/ReelForge.WorkflowEngine/ReelForge.WorkflowEngine.csproj", "src/ReelForge.WorkflowEngine/"]
RUN dotnet restore "src/ReelForge.WorkflowEngine/ReelForge.WorkflowEngine.csproj"
COPY . .
WORKDIR "/src/src/ReelForge.WorkflowEngine"
RUN dotnet build "ReelForge.WorkflowEngine.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "ReelForge.WorkflowEngine.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ReelForge.WorkflowEngine.dll"]
