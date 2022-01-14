using System.Security.Cryptography;
using BlazingPay.WebCommon;
using NBitcoin;
using NBitcoin.Secp256k1;

namespace NNostr.UI.Services;

public class UserRepository
{
    private readonly LocalStorageService _localStorageService;
    private const string USERS_KEY = nameof(UserRepository) + "_Users";
    private string USER_KEY(string user) => $"{nameof(UserRepository)}_User_{user}";

    public UserRepository(LocalStorageService localStorageService)
    {
        _localStorageService = localStorageService;
    }

    public async Task<Dictionary<string, string>?> GetAvailableUsers()
    {
        return await _localStorageService.Get<Dictionary<string, string>>(USERS_KEY) ??
               new Dictionary<string, string>();
    }

    public async Task<User?> GetUser(string userId, string? passphrase)
    {
        return await _localStorageService.Get<User>(USER_KEY(userId), passphrase);
    }

    public async Task SetUser(User user, string? passphrase = null)
    {
        var updated = false;
        var users = await GetAvailableUsers() ?? new Dictionary<string, string>();

        if (users.TryGetValue(user.Key, out var username) && username != user.Username)
        {
            users[user.Key] = user.Username;
            updated = true;
        }
        else if (username is null)
        {
            users.TryAdd(user.Key, user.Username);
            updated = true;
        }

        if (updated)
        {
            await _localStorageService.Set(USERS_KEY, users);
        }

        var key = USER_KEY(user.Key);
        await _localStorageService.Set(key, user, passphrase);
    }
}

public record User
{
    public string? Username { get; set; }
    public string Key { get; set; }
    public ECPrivKey? GetKey() => ECPrivKey.TryCreateFromDer(Convert.FromHexString(Key), out var res) ? res : null;
}