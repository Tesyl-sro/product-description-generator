using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ProductDescGen;

file sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("html_url")] string HtmlUrl);

public sealed record NewVersionInfo(string Latest, string Url);

public sealed class UpdateChecker
{
    private const string GitHubReleasesApiUrl = "https://api.github.com/repos/Tesyl-sro/product-description-generator/releases/latest";

    private readonly string _currentVersion;

    public UpdateChecker(string currentVersion)
    {
        _currentVersion = currentVersion;
    }

    public async Task<NewVersionInfo?> CheckForUpdateAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ProductDescGen"); // GitHub requires a User-Agent

            var release = await http.GetFromJsonAsync<GitHubRelease>(GitHubReleasesApiUrl);

            if (release is null)
            {
                return null;
            }

            var latestStr = release.TagName.TrimStart('v', 'V');

            if (!Version.TryParse(latestStr, out var latest) ||
                !Version.TryParse(_currentVersion.Split('-')[0], out var current))
            {
                return null;
            }

            if (latest <= current)
            {
                return null;
            }

            return new(latest.ToString(), release.HtmlUrl);
        }
        catch
        {
            return null;
        }
    }
}