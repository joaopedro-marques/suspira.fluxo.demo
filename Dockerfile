FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/DemoAgencia.Worker/DemoAgencia.Worker.csproj", "src/DemoAgencia.Worker/"]
RUN dotnet restore "src/DemoAgencia.Worker/DemoAgencia.Worker.csproj"

COPY . .
RUN dotnet publish "src/DemoAgencia.Worker/DemoAgencia.Worker.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl procps && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

RUN mkdir -p /app/logs /app/Assets/agentes

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_PRINT_TELEMETRY_MESSAGE=false

HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD pgrep -f "dotnet DemoAgencia.Worker.dll" || exit 1

ENTRYPOINT ["dotnet", "DemoAgencia.Worker.dll"]
