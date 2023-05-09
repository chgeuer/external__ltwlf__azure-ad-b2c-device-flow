namespace Ltwlf.Azure.B2C;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

public class PageFactory
{
    internal enum PageType
    {
        UserCode,
        Success,
        Error
    }

    private readonly string _userCodePagePath;
    private readonly string _successPagePath;
    private readonly string _errorPagePath;
    private readonly HttpClient _httpClient;

    public PageFactory(IOptions<ConfigOptions> options)
    {
        _httpClient = new();
        var local_root = Environment.GetEnvironmentVariable("AzureWebJobsScriptRoot");
        var azure_root = $"{Environment.GetEnvironmentVariable("HOME")}/site/wwwroot";
        var actual_root = local_root ?? azure_root;

        _userCodePagePath = options.Value.UserCodePage ?? Path.Combine(actual_root, "./www/userCode.html");
        _successPagePath = options.Value.SuccessPage ?? Path.Combine(actual_root, "./www/success.html");
        _errorPagePath = options.Value.ErrorPage ?? Path.Combine(actual_root, "./www/error.html");
    }

    internal async Task<IActionResult> GetPageResult(PageType pageType)
    {
        var path = pageType switch
        {
            PageType.UserCode => _userCodePagePath,
            PageType.Success => _successPagePath,
            PageType.Error => _errorPagePath,
            _ => throw new ArgumentOutOfRangeException(nameof(pageType), pageType, null)
        };

        if (path.StartsWith("http"))
        {
            var content = await _httpClient.GetByteArrayAsync(path);
            return new FileContentResult(content, "text/html; charset=UTF-8");
        }
        else
        {
            return new FileStreamResult(File.OpenRead(path), "text/html; charset=UTF-8");
        }
    }
}
