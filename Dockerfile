ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

COPY ["src/FlowDesk.Domain/FlowDesk.Domain.csproj", "src/FlowDesk.Domain/"]
COPY ["src/FlowDesk.Application/FlowDesk.Application.csproj", "src/FlowDesk.Application/"]
COPY ["src/FlowDesk.Infrastructure/FlowDesk.Infrastructure.csproj", "src/FlowDesk.Infrastructure/"]
COPY ["src/FlowDisk.Api/FlowDisk.Api.csproj", "src/FlowDisk.Api/"]
RUN dotnet restore "src/FlowDisk.Api/FlowDisk.Api.csproj"

COPY . .
RUN dotnet publish "src/FlowDisk.Api/FlowDisk.Api.csproj" \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app
EXPOSE 8080

RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /var/lib/flowdesk \
    && chown "$APP_UID":"$APP_UID" /var/lib/flowdesk

COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .
USER $APP_UID

ENTRYPOINT ["dotnet", "FlowDisk.Api.dll"]
