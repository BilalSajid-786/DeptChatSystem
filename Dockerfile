FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["DeptChat.Domain/DeptChat.Domain.csproj", "DeptChat.Domain/"]
COPY ["DeptChat.Application/DeptChat.Application.csproj", "DeptChat.Application/"]
COPY ["DeptChat.Infrastructure/DeptChat.Infrastructure.csproj", "DeptChat.Infrastructure/"]
COPY ["DeptChat.Shared/DeptChat.Shared.csproj", "DeptChat.Shared/"]
COPY ["DeptChat.WebApi/DeptChat.WebApi.csproj", "DeptChat.WebApi/"]

RUN dotnet restore "DeptChat.WebApi/DeptChat.WebApi.csproj"

COPY . .

WORKDIR "/src/DeptChat.WebApi"
RUN dotnet publish "DeptChat.WebApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 5051
ENV ASPNETCORE_URLS=http://+:5051
ENTRYPOINT ["dotnet", "DeptChat.WebApi.dll"]
