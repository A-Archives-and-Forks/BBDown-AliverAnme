using System.Security.Cryptography;
using BBDown.Core.DRM.Proto;

namespace BBDown.Core.DRM;

public class WvdDevice : IDisposable
{
    public byte[] ClientIdBytes { get; }
    public RSA Rsa { get; }
    public ClientIdentification ClientIdentification { get; }
    private bool _disposed;

    private WvdDevice(byte[] clientIdBytes, RSA rsa, ClientIdentification clientId)
    {
        ClientIdBytes = clientIdBytes;
        Rsa = rsa;
        ClientIdentification = clientId;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Rsa.Dispose();
    }

    public static WvdDevice Load(string path)
    {
        var allBytes = File.ReadAllBytes(path);

        // 格式1: 带 "WVD" magic header (前3字节 = 0x57 0x56 0x44 = "WVD")
        if (allBytes.Length >= 4 && allBytes[0] == 0x57 && allBytes[1] == 0x56 && allBytes[2] == 0x44)
            return ParseWvd(allBytes.AsSpan(3));

        // 格式2: pywidevine 标准格式（无 WVD magic，首字节 = version = 1/2）。
        // ParseWvd 同时支持 v1/v2，探测必须同样放行 v2，否则无 magic 的 v2 文件
        // 会被误判为"无法识别的 WVD 文件格式 (首字节: 2)"。
        if (allBytes.Length >= 1 && allBytes[0] is 1 or 2)
            return ParseWvd(allBytes.AsSpan());

        // 格式3: 纯 PEM 私钥 + 伴生 client_id blob
        if (allBytes.Length > 0 && allBytes[0] == '-')
            return ParsePemPlusClientId(path, allBytes);

        throw new InvalidDataException($"无法识别的 WVD 文件格式 (首字节: {allBytes[0]})");
    }

    private static WvdDevice ParseWvd(Span<byte> data)
    {
        if (data.Length < 6)
            throw new InvalidDataException("WVD 数据长度不足（头部损坏）");

        var version = data[0];
        if (version is not (1 or 2))
            throw new InvalidDataException($"Unsupported WVD version: {version}");

        // V2 may encrypt private key with AES when flags indicate so
        if (version == 2 && (data[3] & 0x01) != 0)
            throw new InvalidDataException("Encrypted WVD V2 private key is not supported yet");

        var type = data[1];
        var securityLevel = data[2];
        var flags = data[3];

        var privateKeyLen = (data[4] << 8) | data[5];
        if (data.Length < 6 + privateKeyLen + 2)
            throw new InvalidDataException("WVD 私钥段长度超出数据范围（文件已损坏或被截断）");

        var privateKeyBytes = data.Slice(6, privateKeyLen).ToArray();
        var offset = 6 + privateKeyLen;

        var clientIdLen = (data[offset] << 8) | data[offset + 1];
        if (data.Length < offset + 2 + clientIdLen)
            throw new InvalidDataException("WVD ClientId 段长度超出数据范围（文件已损坏或被截断）");

        var clientIdBytes = data.Slice(offset + 2, clientIdLen).ToArray();

        return Create(privateKeyBytes, clientIdBytes);
    }

    private static WvdDevice ParsePemPlusClientId(string wvdPath, byte[] allBytes)
    {
        // 尝试在同目录下查找 .client_id 或 _client_id.bin 文件
        var dir = Path.GetDirectoryName(wvdPath) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(wvdPath);

        byte[]? clientIdBytes = null;
        foreach (var candidate in new[] {
            Path.Combine(dir, baseName + "_client_id.bin"),
            Path.Combine(dir, baseName + ".client_id"),
            Path.Combine(dir, "client_id.bin"),
        })
        {
            if (File.Exists(candidate))
            {
                clientIdBytes = File.ReadAllBytes(candidate);
                break;
            }
        }

        if (clientIdBytes == null)
            throw new InvalidDataException("PEM 格式需要配套的 client_id 文件 (_client_id.bin)");

        return Create(allBytes, clientIdBytes);
    }

    private static WvdDevice Create(byte[] privateKeyBytes, byte[] clientIdBytes)
    {
        var rsa = RSA.Create();
        try
        {
            ImportPrivateKey(rsa, privateKeyBytes);

            // 尝试解析 protobuf，兼容非 protobuf 的原始 client_id
            ClientIdentification clientId;
            try
            {
                clientId = ClientIdentification.Parser.ParseFrom(clientIdBytes);
            }
            catch
            {
                // 如果 client_id 不是 protobuf 格式，构建最小 ClientIdentification
                clientId = new ClientIdentification
                {
                    Type = ClientIdentification.Types.TokenType.DrmDeviceCertificate,
                    Token = Google.Protobuf.ByteString.CopyFrom(clientIdBytes),
                };
            }

            return new WvdDevice(clientIdBytes, rsa, clientId);
        }
        catch
        {
            // 私钥导入/解析失败时 RSA 仍持有非托管句柄，必须释放，避免句柄泄漏
            rsa.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 从 .wvd 中的私钥字节导入 RSA 密钥。
    /// 规范的 pywidevine device（Device.dumps）把私钥存为 PKCS#1 DER 二进制，
    /// 原实现只用 ASCII 解码 + ImportFromPem，DER 里 &gt;=0x80 的字节会被解成 '?'，
    /// 导致所有标准 .wvd 文件加载失败（仅仓库内置的非规范 PEM-in-WVD 能用）。
    /// 这里优先按 DER 解析（PKCS#1、再 PKCS#8），失败才回退到 PEM 文本。
    /// </summary>
    internal static void ImportPrivateKey(RSA rsa, byte[] privateKeyBytes)
    {
        // 先判断是不是 PEM 文本：PEM 以 ASCII '-----BEGIN' 开头
        var looksPem = privateKeyBytes.Length > 10
            && System.Text.Encoding.ASCII.GetString(privateKeyBytes, 0, 10) == "-----BEGIN";
        if (looksPem)
        {
            rsa.ImportFromPem(System.Text.Encoding.ASCII.GetString(privateKeyBytes));
            return;
        }

        // 二进制：优先 PKCS#1（pywidevine 用的格式），再试 PKCS#8
        try
        {
            rsa.ImportRSAPrivateKey(privateKeyBytes, out _);
            return;
        }
        catch (CryptographicException) { /* 不是 PKCS#1，继续尝试 */ }

        try
        {
            rsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
            return;
        }
        catch (CryptographicException) { /* 不是 PKCS#8，继续尝试 */ }

        // 最后兜底：可能是缺头尾的 PEM 主体，补全后再试
        var body = System.Text.Encoding.ASCII.GetString(privateKeyBytes);
        rsa.ImportFromPem("-----BEGIN RSA PRIVATE KEY-----\n" + body + "\n-----END RSA PRIVATE KEY-----");
    }
}
