# 构建阶段：Native AOT 发布（Directory.Build.props 已全局启用 PublishAot）。
# 用 AOT 专用 SDK 镜像（预装 AOT 工具链，比通用 sdk 镜像构建更快更小）。
FROM mcr.microsoft.com/dotnet/sdk:10.0-aot AS builder

WORKDIR /src

COPY BBDown.Core/ BBDown.Core/
COPY BBDown/ BBDown/

# 只还原/发布 BBDown 本身：解决方案还含 BBDown.Tests，
# 而这里未复制该项目，用 BBDown.sln 会导致 restore 找不到测试工程而失败。
RUN dotnet restore BBDown/BBDown.csproj
# -r linux-x64 配合 --self-contained 产出单个原生可执行文件（含 AOT 裁剪）。
# PublishAot 由 Directory.Build.props 全局开启，这里无需再传。
RUN dotnet publish BBDown/BBDown.csproj -c Release -r linux-x64 --self-contained -o /app/publish --no-restore

# 运行阶段：runtime-deps 镜像仅含运行原生二进制的系统依赖（glibc 等），
# 体积远小于 aspnet 镜像。BBDown 以原生可执行文件直接启动，不再 dotnet BBDown.dll。
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0

WORKDIR /app

# install ffmpeg
RUN apt-get update && \
    apt-get install -y ffmpeg && \
    rm -rf /var/lib/apt/lists/*

COPY --from=builder /app/publish .

# Native AOT 产物是单个可执行文件（名 BBDown）
RUN chmod +x /app/BBDown

# 以非 root 用户运行：容器内 serve 需要写当前目录（bbdown-tasks.json 等），
# 运行用户对 /app 有读写权。降低容器被攻破后的提权面。
RUN useradd --create-home --uid 10001 bbdown && \
    chown -R bbdown:bbdown /app
USER bbdown

EXPOSE 23333

# 容器默认监听 0.0.0.0：否则 -p 端口映射从宿主访问不到。
# CLI 默认 127.0.0.1 仅本机访问，但容器里必须 0.0.0.0 才能被端口映射转发。
# 安全边界：serve 在非回环监听且未配置 --serve-token 时会拒绝启动（见 ServeCommand），
# 因此直接 docker run <image> 会以错误退出——这是预期行为。对外暴露必须显式传 token：
#   docker run -d --name bbdown -p 23333:23333 <image> serve -l http://0.0.0.0:23333 --serve-token <token>
ENTRYPOINT ["/app/BBDown"]
CMD ["serve", "-l", "http://0.0.0.0:23333"]
