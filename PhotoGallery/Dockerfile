# syntax=docker/dockerfile:1.7

# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore: copy csproj files only for layer caching
COPY Configuration/Configuration.csproj       Configuration/
COPY Authentication/Authentication.csproj     Authentication/
COPY PhotoGallery/PhotoGallery.csproj         PhotoGallery/
RUN dotnet restore PhotoGallery/PhotoGallery.csproj

# Copy full source for the dependency graph
COPY Configuration/   Configuration/
COPY Authentication/  Authentication/
COPY PhotoGallery/    PhotoGallery/

WORKDIR /src/PhotoGallery
RUN dotnet publish PhotoGallery.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# ACA targetPort = 8080. ACA also sets ASPNETCORE_URLS=http://+:8080 via TF,
# but bake a sensible default so the image is correct standalone.
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "PhotoGallery.dll"]
