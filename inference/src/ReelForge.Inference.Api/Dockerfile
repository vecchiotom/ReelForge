FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS base
USER root
# The Data Protection key ring (used to encrypt/decrypt inference-provider API keys, shared with
# the WorkflowEngine via the same volume) must be pre-owned by the non-root app user here, in the
# image, because Docker seeds a fresh named volume's ownership from the image directory at first
# mount — creating it only at container runtime leaves the volume root-owned and unwritable,
# crashing every inference-provider write with an UnauthorizedAccessException.
RUN mkdir -p /keys && chown -R $APP_UID:$APP_UID /keys
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/ReelForge.Shared/ReelForge.Shared.csproj", "src/ReelForge.Shared/"]
COPY ["src/ReelForge.Inference.Api/ReelForge.Inference.Api.csproj", "src/ReelForge.Inference.Api/"]
RUN dotnet restore "src/ReelForge.Inference.Api/ReelForge.Inference.Api.csproj"
COPY . .
WORKDIR "/src/src/ReelForge.Inference.Api"
RUN dotnet build "ReelForge.Inference.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "ReelForge.Inference.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ReelForge.Inference.Api.dll"]
