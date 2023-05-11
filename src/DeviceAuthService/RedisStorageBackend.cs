namespace Ltwlf.Azure.B2C;

using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public interface IStorageBackend
{
    Task<AuthorizationState> GetGetAuthorizationStateByDeviceCode(string deviceCode);

    Task<AuthorizationState> GetGetAuthorizationStateByUserCode(string userCode);

    Task<bool> SetAuthorizationStateAsync(AuthorizationState state);

    Task<bool> DeleteAsync(AuthorizationState state);
}

public class RedisStorageBackend : IStorageBackend
{
    private readonly IConnectionMultiplexer muxer;

    internal RedisStorageBackend(string configuration)
    {
        muxer = ConnectionMultiplexer.Connect(configuration);
    }

    private async Task<T> GetValueByKeyPattern<T>(string pattern)
    {
        async Task<string> GetKeyByPattern(string pattern)
        {
            string key = null;
            var server = muxer.GetServer(muxer.GetEndPoints().Single());
            var enumerator = server.KeysAsync(pattern: pattern).GetAsyncEnumerator();
            while (await enumerator.MoveNextAsync())
            {
                key = enumerator.Current;
                break;
            }

            return key;
        }

        Console.WriteLine($"Searching {pattern}");

        var key = await GetKeyByPattern(pattern);
        if (key == null)
        {
            return default;
        }

        return muxer.GetDatabase().StringGet(key).ToString().DeserializeSnakeCase<T>();
    }

    public Task<AuthorizationState> GetGetAuthorizationStateByDeviceCode(string deviceCode)
        => GetValueByKeyPattern<AuthorizationState>(pattern: $"{deviceCode}:*");

    public Task<AuthorizationState> GetGetAuthorizationStateByUserCode(string userCode)
        => GetValueByKeyPattern<AuthorizationState>(pattern: $"*:{userCode}");

    public Task<bool> SetAuthorizationStateAsync(AuthorizationState state)
    {
        var expiresOn = DateTimeOffset.FromUnixTimeSeconds(state.ExpiresOn);
        var expiry = expiresOn.Subtract(DateTime.UtcNow);

        return muxer.GetDatabase().StringSetAsync(
           key: $"{state.DeviceCode}:{state.UserCode}",
           value: state.SerializeSnakeCase(),
           expiry: expiry);
    }

    public Task<bool> DeleteAsync(AuthorizationState state)
        => muxer.GetDatabase().KeyDeleteAsync(key: $"{state.DeviceCode}:{state.UserCode}");
}

/// <summary>
/// Just a dummy storage backend for development.
/// </summary>
public class SingleInstanceInMemoryBackend : IStorageBackend
{
    private static ConcurrentDictionary<(string DeviceCode, string UserCode), AuthorizationState> _collection = new();

    public Task<AuthorizationState> GetGetAuthorizationStateByDeviceCode(string deviceCode)
        => Task.FromResult(_collection.SingleOrDefault(i => i.Key.DeviceCode == deviceCode).Value);

    public Task<AuthorizationState> GetGetAuthorizationStateByUserCode(string userCode)
        => Task.FromResult(_collection.SingleOrDefault(i => i.Key.UserCode == userCode).Value);

    public Task<bool> SetAuthorizationStateAsync(AuthorizationState state) {
        _collection.AddOrUpdate((state.DeviceCode, state.UserCode), state, (_key, _value) => state);

        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(AuthorizationState state)
        => Task.FromResult(_collection.Remove((state.DeviceCode, state.UserCode), out var _));
}
