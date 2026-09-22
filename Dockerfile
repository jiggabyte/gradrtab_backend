# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["GradrTab.csproj", "./"]
RUN dotnet restore "GradrTab.csproj"
COPY . .
RUN dotnet publish "GradrTab.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Native deps: Tesseract OCR + image libs (Tesseract NuGet needs
# liblept/tesseract shared libs), curl for the HEALTHCHECK.
RUN apt-get update && apt-get install -y --no-install-recommends \
    tesseract-ocr \
    tesseract-ocr-eng \
    libleptonica-dev \
    libtesseract-dev \
    libgdiplus \
    libc6-dev \
    curl \
  && rm -rf /var/lib/apt/lists/*

# Tessdata for the Tesseract NuGet package (TESSDATA_PREFIX fallback)
ENV TESSDATA_PREFIX=/usr/share/tesseract-ocr/5/tessdata
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV ASPNETCORE_ENVIRONMENT=Production
# Render injects PORT at runtime; default keeps `docker run` working without -e PORT.
ENV PORT=8080
EXPOSE 8080

COPY --from=build /app/publish ./

# Writable upload folder (Render disk mounts here, see render.yaml)
RUN mkdir -p /app/Storage/Documents
VOLUME ["/app/Storage"]

# .env is optional at runtime; real env vars (Render dashboard) always win.
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
  CMD curl -fsS "http://localhost:${PORT}/health" || exit 1

ENTRYPOINT ["dotnet", "GradrTab.dll"]
