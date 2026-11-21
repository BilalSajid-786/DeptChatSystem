# DeptChat web app: Blazor WebAssembly client + small host that forwards /api, /hubs and /uploads
# to the API container. Build from the repository root:
#   docker build -f DeptChat.Web/Dockerfile -t deptchat-web .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["DeptChat.Shared/DeptChat.Shared.csproj", "DeptChat.Shared/"]
COPY ["DeptChat.Web.Client/DeptChat.Web.Client.csproj", "DeptChat.Web.Client/"]
COPY ["DeptChat.Web/DeptChat.Web.csproj", "DeptChat.Web/"]

RUN dotnet restore "DeptChat.Web/DeptChat.Web.csproj"

COPY DeptChat.Shared/ DeptChat.Shared/
COPY DeptChat.Web.Client/ DeptChat.Web.Client/
COPY DeptChat.Web/ DeptChat.Web/

RUN dotnet publish "DeptChat.Web/DeptChat.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "DeptChat.Web.dll"]
