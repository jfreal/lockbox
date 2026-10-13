using Lockbox.Core.Import;

namespace Lockbox.Tests;

public sealed class SecretTextParserTests
{
    [Fact]
    public void Groups_pairs_under_headings()
    {
        const string text = """
            acme-web
            NETLIFY_TOKEN=nfp_abc123def456
            DATABASE_URL = "postgres://u:p@db.example.com/acme-web"

            ## billing-api
            export OPENAI_KEY='sk-proj-xyz789'
            Stripe secret: sk_test_51abc
            """;

        var result = SecretTextParser.Parse(text);

        Assert.Equal(["acme-web", "billing-api"], result.Groups.Select(g => g.Heading));
        Assert.Equal(["NETLIFY_TOKEN", "DATABASE_URL"], result.Groups[0].Items.Select(i => i.Name));
        Assert.Equal("postgres://u:p@db.example.com/acme-web", result.Groups[0].Items[1].Value);
        Assert.Equal(["OPENAI_KEY", "Stripe secret"], result.Groups[1].Items.Select(i => i.Name));
        Assert.Equal("sk-proj-xyz789", result.Groups[1].Items[0].Value);
        Assert.Empty(result.Leftovers);
    }

    [Fact]
    public void Label_line_followed_by_bare_token_becomes_one_secret()
    {
        const string text = """
            mobile-app
            github token
            ghp_A1b2C3d4E5f6G7h8I9j0
            """;

        var group = Assert.Single(SecretTextParser.Parse(text).Groups);
        Assert.Equal("mobile-app", group.Heading);
        var item = Assert.Single(group.Items);
        Assert.Equal("github token", item.Name);
        Assert.Equal("ghp_A1b2C3d4E5f6G7h8I9j0", item.Value);
    }

    [Fact]
    public void Bare_url_with_credentials_is_a_secret_not_a_pair()
    {
        var item = Assert.Single(Assert.Single(SecretTextParser.Parse("mongodb+srv://admin:hunter2@cluster0.example.net").Groups).Items);
        Assert.Equal("mongodb+srv URL (cluster0.example.net)", item.Name);
        Assert.Equal("mongodb+srv://admin:hunter2@cluster0.example.net", item.Value);
    }

    [Fact]
    public void Json_and_powershell_styles_are_understood()
    {
        const string text = """
            {
              "apiKey": "AIzaSyD-123456",
              "projectId": "pipeline-prod",
            }
            $env:CLOUDFLARE_API_TOKEN = "cf_abcdef"
            """;

        var items = SecretTextParser.Parse(text).Groups.SelectMany(g => g.Items).ToList();
        Assert.Equal(["apiKey", "projectId", "CLOUDFLARE_API_TOKEN"], items.Select(i => i.Name));
        Assert.Equal(["AIzaSyD-123456", "pipeline-prod", "cf_abcdef"], items.Select(i => i.Value));
    }

    [Fact]
    public void Sentences_become_leftovers_and_prose_values_are_not_preselected()
    {
        const string text = """
            Remember to rotate these every quarter, and check the billing page too.
            Note: the old key was revoked in March after the leak
            """;

        var result = SecretTextParser.Parse(text);
        Assert.Single(result.Leftovers);
        var item = Assert.Single(Assert.Single(result.Groups).Items);
        Assert.False(item.Suggested);
    }

    [Fact]
    public void Value_with_equals_signs_is_kept_whole()
    {
        var item = SecretTextParser.Parse("SIGNING_KEY=abc==").Groups[0].Items[0];
        Assert.Equal("abc==", item.Value);
    }

    [Fact]
    public void Line_numbers_are_one_based()
    {
        var item = SecretTextParser.Parse("\n\nA=1").Groups[0].Items[0];
        Assert.Equal(3, item.LineNumber);
    }
}
