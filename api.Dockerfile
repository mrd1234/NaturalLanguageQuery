FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["NLQueryApp.Api/NLQueryApp.Api.csproj", "NLQueryApp.Api/"]
COPY ["NLQueryApp.Core/NLQueryApp.Core.csproj", "NLQueryApp.Core/"]
COPY ["NLQueryApp.Data/NLQueryApp.Data.csproj", "NLQueryApp.Data/"]
COPY ["NLQueryApp.LlmServices/NLQueryApp.LlmServices.csproj", "NLQueryApp.LlmServices/"]
RUN dotnet restore "NLQueryApp.Api/NLQueryApp.Api.csproj"

# Copy source code and build
COPY . .
WORKDIR "/src/NLQueryApp.Api"
RUN dotnet build "NLQueryApp.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NLQueryApp.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NLQueryApp.Api.dll"]