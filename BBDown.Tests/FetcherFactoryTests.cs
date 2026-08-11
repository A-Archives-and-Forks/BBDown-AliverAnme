using BBDown.Core;
using BBDown.Core.Fetcher;

namespace BBDown.Tests;

/// <summary>
/// FetcherFactory 目标路由测试：空间视频/订阅/列表目标（mid:/favId:/listBizId:/
/// seriesBizId:/ep:/cheese:）必须路由到正确的 fetcher 类型。这是纯本地逻辑，
/// 不发起任何网络请求——验证 UrlResolver 产出的目标前缀与 FetcherFactory 的分发
/// 对齐（路由错位会拿到错误解析器，如把空间目标当普通视频解析）。
/// </summary>
public class FetcherFactoryTests
{
    [Theory]
    [InlineData("mid:12345", typeof(SpaceVideoFetcher))]
    [InlineData("favId:1:2", typeof(FavListFetcher))]
    [InlineData("listBizId:123", typeof(MediaListFetcher))]
    [InlineData("seriesBizId:123", typeof(SeriesListFetcher))]
    [InlineData("ep:12345", typeof(BangumiInfoFetcher))]
    [InlineData("cheese:12345", typeof(CheeseInfoFetcher))]
    [InlineData("170001", typeof(NormalInfoFetcher))]
    public void CreateFetcher_RoutesTargetsToExpectedFetcher(string target, Type expectedType)
    {
        var fetcher = FetcherFactory.CreateFetcher(target, useIntlApi: false);
        Assert.IsType(expectedType, fetcher);
    }

    [Fact]
    public void CreateFetcher_EpWithIntlApi_RoutesToIntlBangumiFetcher()
    {
        // INTL API 下番剧走国际版解析器
        var fetcher = FetcherFactory.CreateFetcher("ep:12345", useIntlApi: true);
        Assert.IsType<IntlBangumiInfoFetcher>(fetcher);
    }

    /// <summary>
    /// 空间/收藏夹/列表目标是订阅体系的核心承载（SubCommand 用 mid:/favId: 等裸前缀）。
    /// 锁死这些前缀的解析不会被 FetcherFactory 误判为普通视频，否则订阅增量下载
    /// 会静默拿错解析器。
    /// </summary>
    [Fact]
    public void CreateFetcher_SubscriptionTargets_AreAllRecognized()
    {
        foreach (var target in new[]
                 {
                     "mid:1", "favId:1:2", "listBizId:1", "seriesBizId:1",
                 })
        {
            var fetcher = FetcherFactory.CreateFetcher(target, useIntlApi: false);
            // 订阅目标不应被路由到普通视频解析器（否则订阅增量下载会静默拿错解析器）
            Assert.False(fetcher is NormalInfoFetcher,
                $"订阅目标 {target} 不应被路由到普通视频解析器");
        }
    }
}
