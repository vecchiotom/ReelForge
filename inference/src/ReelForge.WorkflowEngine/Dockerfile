FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS base
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
