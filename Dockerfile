# Estágio de Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar arquivos de projeto e restaurar dependências
COPY ["Backend/SinistroAPI.csproj", "Backend/"]
RUN dotnet restore "Backend/SinistroAPI.csproj"

# Copiar todo o código fonte
COPY . .

# Publicar a aplicação
WORKDIR "/src/Backend"
RUN dotnet publish "SinistroAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Estágio Final (Runtime)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Copiar a aplicação publicada do estágio de build
COPY --from=build /app/publish .

# Frontend já está em Backend/wwwroot (versão atualizada)

# --- CONFIGURAÇÃO CLOUD RUN ---
# Cria diretório de dados e ajusta permissões (importante para Linux/SQLite)
RUN mkdir -p /data && chmod 777 /data
ENV DB_PATH=/data/sinistros.db

# Otimização de ThreadPool para container
ENV DOTNET_RunningInContainer=true

# Expor a porta 8080 (padrão do Google Cloud Run)
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Comando de entrada
ENTRYPOINT ["dotnet", "SinistroAPI.dll"]
