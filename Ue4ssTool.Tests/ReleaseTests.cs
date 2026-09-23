using ue4ss_tool;

namespace Ue4ssTool.Tests;

/// <summary>GitHub 릴리스 JSON 에서 설치 패키지 고르기. 네트워크는 쓰지 않는다.</summary>
public class ReleaseTests
{
    // 2026-09-19 에 받은 experimental-latest 응답에서 필요한 필드만 남겼다.
    private const string ExperimentalJson = """
        {
          "tag_name": "experimental-latest",
          "prerelease": true,
          "assets": [
            { "name": "UE4SS_v3.0.1-1136-g35d1795d.zip", "size": 8717962, "updated_at": "2026-09-16T20:26:15Z",
              "browser_download_url": "https://github.com/UE4SS-RE/RE-UE4SS/releases/download/experimental-latest/UE4SS_v3.0.1-1136-g35d1795d.zip" },
            { "name": "zCustomGameConfigs.zip", "size": 267526, "updated_at": "2026-09-16T20:26:15Z",
              "browser_download_url": "https://example.invalid/zCustomGameConfigs.zip" },
            { "name": "zDEV-UE4SS_v3.0.1-1136-g35d1795d.zip", "size": 46599228, "updated_at": "2026-09-16T20:26:16Z",
              "browser_download_url": "https://example.invalid/zDEV-UE4SS_v3.0.1-1136-g35d1795d.zip" },
            { "name": "zMapGenBP.zip", "size": 27568, "updated_at": "2026-09-16T20:26:15Z",
              "browser_download_url": "https://example.invalid/zMapGenBP.zip" },
            { "name": "UE4SS_v3.0.1-1100-gaaaaaaa.zip", "size": 1, "updated_at": "2026-08-01T00:00:00Z",
              "browser_download_url": "https://example.invalid/old.zip" }
          ]
        }
        """;

    private const string StableJson = """
        {
          "tag_name": "v3.0.1",
          "prerelease": false,
          "assets": [
            { "name": "UE4SS_v3.0.1.zip", "size": 5523402, "updated_at": "2024-02-14T19:59:38Z",
              "browser_download_url": "https://example.invalid/UE4SS_v3.0.1.zip" },
            { "name": "zDEV-UE4SS_v3.0.1.zip", "size": 23810106, "updated_at": "2024-02-14T19:59:38Z",
              "browser_download_url": "https://example.invalid/zDEV-UE4SS_v3.0.1.zip" }
          ]
        }
        """;

    [Fact]
    public void 부속_zip은_빼고_기본과_zDEV를_최신_하나씩_고른다()
    {
        var packages = Ue4ssReleases.ParseRelease(ExperimentalJson);

        Assert.Equal(
            ["UE4SS_v3.0.1-1136-g35d1795d.zip", "zDEV-UE4SS_v3.0.1-1136-g35d1795d.zip"],
            packages.Select(p => p.AssetName));
        Assert.All(packages, p => Assert.True(p.IsExperimental));
        Assert.False(packages[0].IsDev);
        Assert.True(packages[1].IsDev);
        Assert.Equal("v3.0.1-1136-g35d1795d", packages[0].VersionText);
        Assert.Equal(8717962, packages[0].Size);
    }

    [Fact]
    public void 안정판은_prerelease가_아니다()
    {
        var packages = Ue4ssReleases.ParseRelease(StableJson);

        Assert.Equal(2, packages.Count);
        Assert.All(packages, p => Assert.False(p.IsExperimental));
        Assert.Equal("v3.0.1", packages[0].VersionText);
        Assert.Equal("안정판 v3.0.1", packages[0].Label);
        Assert.Equal("안정판 개발자용(zDEV) v3.0.1", packages[1].Label);
        Assert.Contains("UE 4.11~5.3", packages[0].Details);
    }

    [Fact]
    public void 받기_전에_배치를_짐작한다()
    {
        Assert.All(Ue4ssReleases.ParseRelease(ExperimentalJson), p => Assert.Equal(Ue4ssLayout.Subfolder, p.ExpectedLayout));
        Assert.All(Ue4ssReleases.ParseRelease(StableJson), p => Assert.Equal(Ue4ssLayout.Flat, p.ExpectedLayout));

        // 앞으로 나올 안정판은 배치를 모른다 → 받은 뒤 zip 을 보고 정한다.
        var future = new Ue4ssPackage("v4.0.0", false, false, "UE4SS_v4.0.0.zip", 1, DateTimeOffset.Now, "");
        Assert.Equal(Ue4ssLayout.None, future.ExpectedLayout);
    }

    [Fact]
    public async Task 캐시에_크기가_맞는_zip이_있으면_네트워크_없이_그것을_쓴다()
    {
        var cache = Path.Combine(Path.GetTempPath(), "ue4ss-tool-tests", Guid.NewGuid().ToString("N"));
        var saved = Ue4ssReleases.CacheDir;
        try
        {
            Ue4ssReleases.CacheDir = cache;
            var zip = FakeZip.Subfolder(Path.Combine(cache, "experimental-latest"));
            var name = Path.GetFileName(zip);
            var target = Path.Combine(cache, "experimental-latest", "UE4SS_v9.9.9.zip");
            File.Move(zip, target);

            var cached = Assert.Single(Ue4ssReleases.FromCache());
            Assert.True(cached.IsExperimental);
            Assert.Equal("", cached.DownloadUrl);

            // 주소가 비어 있어도 캐시 파일이 있으면 받지 않고 돌려준다.
            Assert.Equal(target, await Ue4ssReleases.DownloadAsync(cached));
        }
        finally
        {
            Ue4ssReleases.CacheDir = saved;
            try { Directory.Delete(cache, recursive: true); } catch { }
        }
    }
}
