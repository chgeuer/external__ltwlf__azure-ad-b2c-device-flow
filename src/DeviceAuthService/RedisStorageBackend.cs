namespace Ltwlf.Azure.B2C;

using StackExchange.Redis;
using System;
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

//public record StorageIdentifier(string DeviceCode, string UserCode, DateTimeOffset Expires);
//public class InMemoryBackend : IStorageBackend
//{
//    private static ConcurrentDictionary<StorageIdentifier, AuthorizationState> _collection = new();
//    public Task<bool> DeleteAsync(AuthorizationState state)
//    {
//        var x = _collection.FirstOrDefault(x => x.Key.UserCode == state.UserCode && x.Key.DeviceCode == state.DeviceCode);
//        throw new NotImplementedException();
//        //return Task.FromResult(_collection.TryRemove(state.GetStorageIdentifier(), out var _ignore));
//    }
//    public Task<AuthorizationState> GetGetAuthorizationStateByDeviceCode(string deviceCode) => throw new NotImplementedException();
//    public Task<AuthorizationState> GetGetAuthorizationStateByUserCode(string userCode) => throw new NotImplementedException();
//    public Task<bool> SetAuthorizationStateAsync(AuthorizationState state, int expiryInSeconds) => throw new NotImplementedException();
//}
