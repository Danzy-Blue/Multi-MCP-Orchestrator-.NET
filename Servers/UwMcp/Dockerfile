FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Servers/UwMcp/UwMcp.csproj", "Servers/UwMcp/"]
COPY ["Servers/UwMcp/UwMcp.Application/UwMcp.Application.csproj", "Servers/UwMcp/UwMcp.Application/"]
RUN dotnet restore "Servers/UwMcp/UwMcp.csproj"

COPY Servers/UwMcp/ Servers/UwMcp/
RUN dotnet publish "Servers/UwMcp/UwMcp.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV PORT=8001
ENV MCP_HTTP_HOST=0.0.0.0
ENV MCP_HTTP_PATH=/mcp

COPY --from=build /app/publish .

EXPOSE 8001

ENTRYPOINT ["dotnet", "UwMcp.dll"]
