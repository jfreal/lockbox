using Lockbox.Core;

namespace Lockbox.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void Reads_personal_dropbox_path()
    {
        const string json = """{"personal": {"path": "D:\\Dropbox", "host": 1, "is_team": false}}""";
        Assert.Equal(@"D:\Dropbox", AppPaths.ParseDropboxInfo(json));
    }

    [Fact]
    public void Falls_back_to_business_account()
    {
        const string json = """{"business": {"path": "E:\\Dropbox (Acme)"}}""";
        Assert.Equal(@"E:\Dropbox (Acme)", AppPaths.ParseDropboxInfo(json));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"personal": {"path": ""}}""")]
    public void Unusable_info_returns_null(string json) => Assert.Null(AppPaths.ParseDropboxInfo(json));
}
