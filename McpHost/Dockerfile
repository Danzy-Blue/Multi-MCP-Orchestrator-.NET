FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["McpHost/McpHost.csproj", "McpHost/"]
COPY ["McpHost/McpHost.Application/McpHost.Application.csproj", "McpHost/McpHost.Application/"]
COPY ["McpHost/McpHost.Infrastructure/McpHost.Infrastructure.csproj", "McpHost/McpHost.Infrastructure/"]
COPY ["McpGateway/McpGateway.csproj", "McpGateway/"]
RUN dotnet restore "McpHost/McpHost.csproj"

COPY McpHost/ McpHost/
COPY McpGateway/ McpGateway/
RUN dotnet publish "McpHost/McpHost.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV API_PORT=8888

COPY --from=build /app/publish .

EXPOSE 8888

ENTRYPOINT ["dotnet", "McpHost.dll"]
