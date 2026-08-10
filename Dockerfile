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

# 容器默认监听 0.0.0.0：否则 -p 端口映射从宿主访问不到。
# CLI 默认 127.0.0.1 仅本机访问，但容器里必须 0.0.0.0 才能被端口映射转发。
# 对外暴露时务必追加 --serve-token：docker run -p 23333:23333 <image> serve -l http://0.0.0.0:23333 --serve-token <token>
ENTRYPOINT ["dotnet", "BBDown.dll"]
CMD ["serve", "-l", "http://0.0.0.0:23333"]