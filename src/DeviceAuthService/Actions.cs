namespace Ltwlf.Azure.B2C;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web;

public class Actions
{
    static IActionResult Ok(object o) => new OkObjectResult(o);
    static IActionResult Error(string errorCode) => new BadRequestObjectResult(new TokenResponseError(Error: errorCode));

    private readonly IStorageBackend storage;
    private readonly HttpClient _client;
    private readonly PageFactory _pageFactory;
    private readonly ConfigOptions _config;
    private static readonly RandomNumberGenerator _random = RandomNumberGenerator.Create();

    public Actions(IStorageBackend storage, HttpClient client, IOptions<ConfigOptions> options, PageFactory pageFactory)
        => (this.storage, _client, _config, _pageFactory) = (storage, client, options.Value, pageFactory);

    [FunctionName("user_code_get")]
    public Task<IActionResult> UserCodeGetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "/")] HttpRequest req,
        ILogger log,
        ExecutionContext context)
    {
        return _pageFactory.GetPageResult(PageFactory.PageType.UserCode);
    }

    [FunctionName("user_code_post")]
    public async Task<IActionResult> UserCodePostAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "/")] HttpRequest req,
        ILogger log,
        ExecutionContext context)
    {
        var useAjax = req.Headers.TryGetValue("x-use-ajax", out var useAjaxHeader) &&
                      string.Equals(useAjaxHeader, "true", StringComparison.OrdinalIgnoreCase);

        try
        {
            var userCode = req.Form["user_code"];
            var authState = await storage.GetGetAuthorizationStateByUserCode(userCode);
            var tenant = _config.Tenant;
            var policy = _config.SignInPolicy;
            var appId = _config.AppId;
            var redirectUri = HttpUtility.UrlEncode(_config.RedirectUri);
            var scope = authState.Scope ?? "openid";

            var url =
                $"https://{tenant}.b2clogin.com/{tenant}.onmicrosoft.com/oauth2/v2.0/authorize?p={policy}&client_Id={appId}&redirect_uri={redirectUri}&scope={scope}&state={authState.UserCode}&nonce=defaultNonce&response_type=code&prompt=login&response_mode=form_post";

            return useAjax
                ? (IActionResult)new ContentResult { Content = url, ContentType = "text/plain" }
                : new RedirectResult(url);
        }
        catch (Exception e)
        {
            log.LogInformation(e.Message);

            if (useAjax)
            {
                return new BadRequestResult();
            }

            var filePath = _config.ErrorPage ?? Path.Combine(context.FunctionDirectory, "../www/error.html");
            if (filePath.StartsWith("http"))
            {
                var content = await _client.GetByteArrayAsync(filePath);
                return new FileContentResult(content, contentType: "text/html; charset=UTF-8");
            }
            else
            {
                return new FileStreamResult(File.OpenRead(filePath), "text/html; charset=UTF-8");
            }
        }
    }

    [FunctionName("authorization_callback")]
    public async Task<IActionResult> AuthorizationCallbackPostAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "authorization_callback")] HttpRequest req,
        ILogger log)
    {
        log.LogInformation("authorization_callback function processed a request.");

        if (req.Query.ContainsKey("error"))
        {
            return await _pageFactory.GetPageResult(PageFactory.PageType.Error);
        }

        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        var parsedBody = HttpUtility.ParseQueryString(requestBody);
        string code = parsedBody.Get("code");
        string userCode = parsedBody.Get("state");

        var authState = await storage.GetGetAuthorizationStateByUserCode(userCode);
        if (authState == null)
        {
            log.LogWarning("Device authentication request expired");
            return await _pageFactory.GetPageResult(PageFactory.PageType.Error);
        }

        var scope = authState.Scope ?? "openid";

        var httpResponseMessage = await _client.PostAsync(
            requestUri: _config.GetTokenEndpoint(),
            content: new FormUrlEncodedContent(new KeyValuePair<string, string>[]
                {
                    new ("client_id", _config.AppId),
                    new ("client_secret", _config.AppSecret),
                    new ("grant_type", "authorization_code"),
                    new ("code", code),
                    new ("scope", scope),
                }));

        if (httpResponseMessage.IsSuccessStatusCode == false)
        {
            var errorContent = await httpResponseMessage.Content.ReadAsStringAsync();
            return new ContentResult() { Content = errorContent };
        }

        var tokenResponse = await httpResponseMessage.Content.ReadAsJsonAsync<TokenResponse>();

        await storage.SetAuthorizationStateAsync(authState with { TokenResponse = tokenResponse });

        return await _pageFactory.GetPageResult(PageFactory.PageType.Success);
    }

    [FunctionName("device_authorization")]
    public async Task<IActionResult> DeviceAuthorizationPostAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "oauth/device_authorization")] HttpRequest req,
        ILogger log)
    {
        log.LogInformation("DeviceAuthorization function is processing a request.");

        if (req.ContentLength == null || !string.Equals(req.ContentType, "application/x-www-form-urlencoded", StringComparison.InvariantCultureIgnoreCase))
        {
            throw new ArgumentException("Request content type must be application/x-www-form-urlencoded");
        }

        static string GenerateDeviceCode()
        {
            // Guid.NewGuid().ToString();
            var deviceCodeBytes = new byte[16];
            _random.GetBytes(deviceCodeBytes);
            return new Guid(deviceCodeBytes).ToString();
        }

        string GenerateUserCode()
        {
            // modulus = Math.Pow(10, _config.UserCodeLength);
            ulong modulus = 1;
            for (var i = 0; i < _config.UserCodeLength; i++)
            {
                modulus *= 10;
            }

            var bytes = new byte[sizeof(long)];
            _random.GetBytes(bytes);
            var userCode = BitConverter.ToUInt64(bytes) % modulus;
            return userCode.ToString($"D{_config.UserCodeLength}");
        }

        TimeSpan validity = TimeSpan.FromSeconds(_config.ExpiresIn);
        var expiresIn = (int)validity.TotalSeconds;
        var expiresOn = (int)DateTime.UtcNow.Add(validity).Subtract(DateTime.UnixEpoch).TotalSeconds;
        var deviceCode = GenerateDeviceCode();
        var userCode = GenerateUserCode();
        var verificationUri = _config.VerificationUri;
        var scope = req.Form?["scope"];

        await storage.SetAuthorizationStateAsync(new AuthorizationState(
            DeviceCode: deviceCode, UserCode: userCode, VerificationUri: verificationUri,
            Scope: scope, TokenResponse: null, ExpiresOn: expiresOn));

        return Ok(new AuthorizationResponse(
            DeviceCode: deviceCode, UserCode: userCode, VerificationUri: verificationUri,
            ExpiresIn: expiresIn, ExpiresOn: expiresOn,
            Message: $"Please sign-in at {_config.VerificationUri} using code {userCode}", Interval: 5));
    }

    [FunctionName("token")]
    public async Task<IActionResult> TokenGetPostAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "oauth/token")] HttpRequest req,
        ILogger _log)
    {
        var deviceCode = req.Form["device_code"].SingleOrDefault();

        var grantType = req.Form["grant_type"].SingleOrDefault();
        if (string.IsNullOrEmpty(grantType))
        {
            return new BadRequestObjectResult("grant_type are mandatory");
        }

        if (string.Equals(grantType, "refresh_token", StringComparison.OrdinalIgnoreCase))
        {
            FormUrlEncodedContent data = new(new KeyValuePair<string, string>[]
               {
                    new ("client_id", _config.AppId),
                    new ("client_secret", _config.AppSecret),
                    new ("grant_type", "refresh_token"),
                    new ("refresh_token", req.Form["refresh_token"]),
                    new ("scope", req.Form["scope"].SingleOrDefault() ?? "openid"),
               });

            var b2cHttpResponseMessage = await _client.PostAsync(_config.GetTokenEndpoint(), content: data);

            var tokenResponse = await b2cHttpResponseMessage.Content.ReadAsJsonAsync<TokenResponse>();

            return Ok(tokenResponse.PruneResponseForClient());
        }
        else
        {
            async Task<IActionResult> DeleteStateAndReturnPrunedTokenResponse(AuthorizationState state)
            {
                await storage.DeleteAsync(state);
                return Ok(state.TokenResponse.PruneResponseForClient());
            }

            var authState = await storage.GetGetAuthorizationStateByDeviceCode(deviceCode);
            return authState switch
            {
                null => Error("expired_token"),
                { TokenResponse: null } => Error("authorization_pending"),
                var state => await DeleteStateAndReturnPrunedTokenResponse(state),
            };
        }
    }
}

internal static class MyExtensions
{
    internal static string GetTokenEndpoint(this ConfigOptions o) => $"https://{o.Tenant}.b2clogin.com/{o.Tenant}.onmicrosoft.com/{o.SignInPolicy}/oauth2/v2.0/token";

    internal static TokenResponse PruneResponseForClient(this TokenResponse t) => t with { IdentityToken = null, IdentityTokenExpiresIn = null, ProfileInfo = null };

    internal static async Task<T> ReadAsJsonAsync<T>(this HttpContent content, JsonSerializerSettings jsonSerializerSettings = null)
    {
        jsonSerializerSettings ??= new JsonSerializerSettings().ConfigureSnakeCaseNamingAndIgnoringNullValues();
        string json = await content.ReadAsStringAsync();
        T value = JsonConvert.DeserializeObject<T>(json, jsonSerializerSettings);
        return value;
    }

    static readonly JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings().ConfigureSnakeCaseNamingAndIgnoringNullValues();

    internal static JsonSerializerSettings ConfigureSnakeCaseNamingAndIgnoringNullValues(this JsonSerializerSettings s)
    {
        s.ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() };
        s.NullValueHandling = NullValueHandling.Ignore;
        s.Formatting = Formatting.Indented;
        return s;
    }

    internal static string SerializeSnakeCase<T>(this T t) => JsonConvert.SerializeObject(t, settings: jsonSerializerSettings);

    internal static T DeserializeSnakeCase<T>(this string s) => JsonConvert.DeserializeObject<T>(value: s, settings: jsonSerializerSettings);
}