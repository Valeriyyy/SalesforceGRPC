FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5000 5001

# Use amd64 platform for build stage to avoid protoc ARM64 issues
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["SalesforceGrpc/SalesforceGrpc.csproj", "SalesforceGrpc/"]
COPY ["Database/Database.csproj", "Database/"]
RUN dotnet restore "SalesforceGrpc/SalesforceGrpc.csproj"
COPY . .
WORKDIR "/src/SalesforceGrpc"
RUN dotnet publish "./SalesforceGrpc.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:5000;https://+:5001
ENV ASPNETCORE_ENVIRONMENT=Production
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SalesforceGrpc.dll"]
