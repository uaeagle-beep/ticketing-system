# syntax=docker/dockerfile:1

# ---------- Build stage: .NET 10 SDK ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (better layer caching). Copy solution + project files + nuget config.
COPY nuget.config ./
COPY TicketTracker.sln ./
COPY src/TicketTracker.Domain/TicketTracker.Domain.csproj src/TicketTracker.Domain/
COPY src/TicketTracker.Application/TicketTracker.Application.csproj src/TicketTracker.Application/
COPY src/TicketTracker.Infrastructure/TicketTracker.Infrastructure.csproj src/TicketTracker.Infrastructure/
COPY src/TicketTracker.Api/TicketTracker.Api.csproj src/TicketTracker.Api/
RUN dotnet restore src/TicketTracker.Api/TicketTracker.Api.csproj

# Copy the rest and publish.
COPY . .
RUN dotnet publish src/TicketTracker.Api/TicketTracker.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---------- Runtime stage: ASP.NET Core 10 runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Non-root user (ADR-0005). The aspnet image ships a pre-created 'app' user.
USER app

# ASP.NET Core listens on http://+:8080 inside the container (canonical topology).
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "TicketTracker.Api.dll"]
