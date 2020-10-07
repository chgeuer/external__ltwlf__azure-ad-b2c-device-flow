﻿using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;

namespace Ltwlf.Azure.B2C
{
    public class AuthorizationCallback
    {
        private readonly IConnectionMultiplexer _muxer;
        private readonly HttpClient _client;
        private readonly ConfigOptions _config;

        public AuthorizationCallback(IConnectionMultiplexer muxer, HttpClient client, IOptions<ConfigOptions> options)
        {
            _muxer = muxer;
            _client = client;
            _config = options.Value;
        }

        [FunctionName("authorization_callback")]
        public async Task<IActionResult> RunAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "oauth/authorization_callback")]
            HttpRequest req, ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string code = req.Query["code"];
            string userCode = req.Query["state"];
            

            var authState = await Helpers.GetValueByKeyPattern<AuthorizationState>(_muxer, $"*:{userCode}");
            if (authState == null)
            {
                throw new NullReferenceException("Device authentication request expired");
            }
            
            var scope = authState.Scope ?? "openid";
            
            var tokenResponse = await _client.PostAsync(Helpers.GetTokenEndpoint(_config),
                new StringContent(
                        $"grant_type=authorization_code&client_id={_config.AppId}&client_secret={_config.AppSecret}&scope={scope}&code={code}")
                    {Headers = {ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded")}});

            var jwt = await tokenResponse.Content.ReadAsAsync<JObject>();

            authState.AccessToken = jwt.Value<string>("access_token");
            authState.RefreshToken = jwt.Value<string>("refresh_token");
            authState.ExpiresIn = jwt.Value<int>("expires_in");
            authState.TokenType = jwt.Value<string>("token_type");

            _muxer.GetDatabase().StringSet($"{authState.DeviceCode}:{authState.UserCode}",
                JsonConvert.SerializeObject(authState));

            return new OkResult();
        }
        
    }
}