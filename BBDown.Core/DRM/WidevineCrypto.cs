using System.Security.Cryptography;

namespace BBDown.Core.DRM;

/// <summary>
/// Widevine 专用密码学助手。参考 pywidevine / Google Widevine 标准。
/// </summary>
internal static class WidevineCrypto
{
    /// <summary>
    /// AES-CMAC（RFC 4493）
    /// </summary>
    public static byte[] AesCmac(byte[] key, byte[] message)
    {
        // 1. 子密钥生成
        var zero = new byte[16];
        var l = AesEcbEncrypt(zero, key);
        var k1 = SubKey(l);
        var k2 = SubKey(k1);

        // 2. 计算块数
        var n = (message.Length + 15) / 16;
        if (n == 0) n = 1;
        var lastComplete = (message.Length > 0 && message.Length % 16 == 0);

        // 3. 构造最后一块
        var lastBlock = new byte[16];
        if (lastComplete)
        {
            var start = message.Length - 16;
            Buffer.BlockCopy(message, start, lastBlock, 0, 16);
            XorInPlace(lastBlock, k1);
        }
        else
        {
            var partialLen = message.Length % 16;
            var start = message.Length - partialLen;
            Buffer.BlockCopy(message, start, lastBlock, 0, partialLen);
            lastBlock[partialLen] = 0x80;
            XorInPlace(lastBlock, k2);
        }

        // 4. 迭代加密
        var x = new byte[16];
        for (var i = 0; i < n - 1; i++)
        {
            var block = new byte[16];
            Buffer.BlockCopy(message, i * 16, block, 0, 16);
            XorInPlace(x, block);
            x = AesEcbEncrypt(x, key);
        }

        XorInPlace(x, lastBlock);
        return AesEcbEncrypt(x, key);
    }

    private static byte[] SubKey(byte[] key)
    {
        var result = new byte[16];
        var carry = 0;
        for (var i = 15; i >= 0; i--)
        {
            var b = (key[i] << 1) | carry;
            result[i] = (byte)b;
            carry = (key[i] & 0x80) != 0 ? 1 : 0;
        }
        if ((key[0] & 0x80) != 0)
            result[15] ^= 0x87;
        return result;
    }

    private static void XorInPlace(byte[] a, byte[] b)
    {
        for (var i = 0; i < a.Length; i++)
            a[i] ^= b[i];
    }

    // codeql[cs/ecb-encryption] 单块（16 字节）ECB 是协议要求的原语：CMAC 子密钥生成/主体
    // （NIST SP 800-38B）与零 IV 单块解密（CBC 退化为单块 AES）均固定单块操作，
    // 不存在多块 ECB 的模式泄露问题。勿改为 CBC/CTR。
    public static byte[] AesEcbEncrypt(byte[] data, byte[] key)
    {
        if (data.Length != 16)
            throw new ArgumentException("ECB block must be exactly 16 bytes", nameof(data));
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var enc = aes.CreateEncryptor();
        return enc.TransformFinalBlock(data, 0, data.Length);
    }

    public static byte[] AesCbcEncrypt(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var enc = aes.CreateEncryptor();
        return enc.TransformFinalBlock(data, 0, data.Length);
    }

    public static byte[] AesCbcDecrypt(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(data, 0, data.Length);
    }

    // codeql[cs/ecb-encryption] 同上：仅用于零 IV 单块内容密钥解密（见 WidevineCdm.cs
    // isZeroIv 分支），单块 AES 与 CBC 等价，无模式泄露。
    public static byte[] AesEcbDecrypt(byte[] data, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(data, 0, data.Length);
    }

    public static byte[] Pkcs7Pad(byte[] data, int blockSize)
    {
        var pad = (byte)(blockSize - (data.Length % blockSize));
        if (pad == 0) pad = (byte)blockSize;
        var result = new byte[data.Length + pad];
        Buffer.BlockCopy(data, 0, result, 0, data.Length);
        Array.Fill(result, pad, data.Length, pad);
        return result;
    }

    public static byte[] Pkcs7Unpad(byte[] data)
    {
        if (data.Length == 0)
            throw new InvalidDataException("empty data for PKCS7 unpad");
        var pad = data[^1];
        if (pad == 0 || pad > 16 || pad > data.Length)
            throw new InvalidDataException("bad PKCS7 padding");
        for (var i = data.Length - pad; i < data.Length; i++)
        {
            if (data[i] != pad)
                throw new InvalidDataException("inconsistent PKCS7 padding");
        }
        var result = new byte[data.Length - pad];
        Buffer.BlockCopy(data, 0, result, 0, result.Length);
        return result;
    }

    /// <summary>
    /// 派生 Widevine context 数据。
    /// </summary>
    public static (byte[] encContext, byte[] macContext) DeriveContext(byte[] message)
    {
        var encLabel = System.Text.Encoding.ASCII.GetBytes("ENCRYPTION\x00");
        var macLabel = System.Text.Encoding.ASCII.GetBytes("AUTHENTICATION\x00");

        var encContext = new byte[encLabel.Length + message.Length + 4];
        Buffer.BlockCopy(encLabel, 0, encContext, 0, encLabel.Length);
        Buffer.BlockCopy(message, 0, encContext, encLabel.Length, message.Length);
        // key_size = 128 bits = 16 bytes → 4 bytes big-endian
        encContext[encContext.Length - 4] = 0x00;
        encContext[encContext.Length - 3] = 0x00;
        encContext[encContext.Length - 2] = 0x00;
        encContext[encContext.Length - 1] = 0x80;

        var macContext = new byte[macLabel.Length + message.Length + 4];
        Buffer.BlockCopy(macLabel, 0, macContext, 0, macLabel.Length);
        Buffer.BlockCopy(message, 0, macContext, macLabel.Length, message.Length);
        // key_size = 512 bits = 64 bytes → 4 bytes big-endian
        macContext[macContext.Length - 4] = 0x00;
        macContext[macContext.Length - 3] = 0x00;
        macContext[macContext.Length - 2] = 0x02;
        macContext[macContext.Length - 1] = 0x00;

        return (encContext, macContext);
    }

    /// <summary>
    /// 从 session_key 派生加密/HMAC 密钥。
    /// </summary>
    public static (byte[] encKey, byte[] macKeyServer, byte[] macKeyClient) DeriveKeys(byte[] sessionKey, byte[] encContext, byte[] macContext)
    {
        // derive(counter) = AES-CMAC(sessionKey, counter || context)
        byte[] Derive(byte[] context, int counter)
        {
            var input = new byte[1 + context.Length];
            input[0] = (byte)counter;
            Buffer.BlockCopy(context, 0, input, 1, context.Length);
            return AesCmac(sessionKey, input);
        }

        var encKey = Derive(encContext, 1);

        var macKeyServer1 = Derive(macContext, 1);
        var macKeyServer2 = Derive(macContext, 2);
        var macKeyServer = new byte[macKeyServer1.Length + macKeyServer2.Length];
        Buffer.BlockCopy(macKeyServer1, 0, macKeyServer, 0, macKeyServer1.Length);
        Buffer.BlockCopy(macKeyServer2, 0, macKeyServer, macKeyServer1.Length, macKeyServer2.Length);

        var macKeyClient1 = Derive(macContext, 3);
        var macKeyClient2 = Derive(macContext, 4);
        var macKeyClient = new byte[macKeyClient1.Length + macKeyClient2.Length];
        Buffer.BlockCopy(macKeyClient1, 0, macKeyClient, 0, macKeyClient1.Length);
        Buffer.BlockCopy(macKeyClient2, 0, macKeyClient, macKeyClient1.Length, macKeyClient2.Length);

        return (encKey, macKeyServer, macKeyClient);
    }
}
