FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

COPY BBDown.Core/ BBDown.Core/
COPY BBDown/ BBDown/

# 只还原/发布 BBDown 本身：解决方案还含 BBDown.Tests，
# 而这里未复制该项目，用 BBDown.sln 会导致 restore 找不到测试工程而失败。
RUN dotnet restore BBDown/BBDown.csproj
RUN dotnet publish BBDown/BBDown.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app

# install ffmpeg
RUN apt-get update && \
    apt-get install -y ffmpeg && \
    rm -rf /var/lib/apt/lists/*

COPY --from=builder /app/publish .

EXPOSE 23333

ENTRYPOINT ["dotnet", "BBDown.dll", "serve", "-l", "http://0.0.0.0:23333"]