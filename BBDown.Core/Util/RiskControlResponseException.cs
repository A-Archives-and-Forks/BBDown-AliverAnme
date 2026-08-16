using System.Text.Json;

namespace BBDown.Core.Util;

/// <summary>
/// 接口返回 HTTP 200 但响应体是 HTML 页面而非预期数据——B 站风控/登录墙/错误页
/// 常以 200 携带 HTML 返回，下游 JsonDocument.Parse / grpc 反序列化只能报出难以定位的
/// 裸解析错误。此异常给出可读诊断，替代裸 <see cref="JsonException"/>。
/// 继承 <see cref="JsonException"/> 而非 InvalidOperationException：既有代码里大量
/// "装饰性抓取"（字幕/章节/直播信息等）用 catch (JsonException) 做优雅降级——
/// 继承 JsonException 让这些降级点**原样生效**（HTML 视为一次解析失败），无需逐个改 catch；
/// 而 JSON 解析不可能以 '<' 开头，JsonException 的语义（"这不是合法 JSON 响应"）也吻合。
/// </summary>
public sealed class RiskControlResponseException : JsonException
{
    public RiskControlResponseException(string url)
        : base($"疑似风控页：接口返回 HTML 而非预期数据（{SensitiveDataMasker.MaskUrl(url)}）。" +
               "可能被 B 站风控拦截，请稍后重试或检查账号/网络状态。")
    {
    }
}