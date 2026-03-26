FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Servers/DevOpsMcp/DevOpsMcp.csproj", "Servers/DevOpsMcp/"]
COPY ["Servers/DevOpsMcp/DevOpsMcp.Application/DevOpsMcp.Application.csproj", "Servers/DevOpsMcp/DevOpsMcp.Application/"]
RUN dotnet restore "Servers/DevOpsMcp/DevOpsMcp.csproj"

COPY Servers/DevOpsMcp/ Servers/DevOpsMcp/
RUN dotnet publish "Servers/DevOpsMcp/DevOpsMcp.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV PORT=8002
ENV MCP_HTTP_HOST=0.0.0.0
ENV MCP_HTTP_PATH=/mcp

COPY --from=build /app/publish .

EXPOSE 8002

ENTRYPOINT ["dotnet", "DevOpsMcp.dll"]
