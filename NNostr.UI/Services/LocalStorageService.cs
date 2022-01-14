using System.Text.Json;
using BlazingPay.Abstractions.Services;
using Microsoft.JSInterop;

namespace BlazingPay.WebCommon
{
    public class SessionStorageService:LocalStorageService
    {
        public SessionStorageService(IJSRuntime jsRuntime) : base(jsRuntime)
        {
        }

        protected override string jsNamespace => "sessionStorage";
    }

    public class LocalStorageService
    {
        protected virtual string jsNamespace => "localStorage";
        private readonly IJSRuntime _jsRuntime;

        public LocalStorageService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public virtual async Task<T?> Get<T>(string key, string? passphrase = null)
        {
            var lsRes = await GetRaw(key, passphrase);

            return lsRes is null ? default : JsonSerializer.Deserialize<T>(lsRes);
        }

        protected async Task<string?> GetRaw(string key, string? passphrase = null)
        {
            var rawResult =  await _jsRuntime.InvokeAsync<string?>($"{jsNamespace}.getItem", key);
            if (rawResult?.StartsWith("encrypted:") is not true) return rawResult;
            if (passphrase is null)
            {
                throw new ArgumentNullException(nameof(passphrase),
                    "The key {key} holds encrypted data but you did not provide a passphrase");
            }
            rawResult = DataEncryptor.Decrypt(rawResult, passphrase);
            return rawResult;
        }

        public virtual async Task Set<T>(string key, T value, string? passphrase = null)
        {
            var defaultValue = default(T);
            
            if ((value is null && defaultValue is null) || (value?.Equals(default(T))??false))
            {
                await _jsRuntime.InvokeVoidAsync($"{jsNamespace}.removeItem", key);
            }
            else
            {
                await SetRaw(key, JsonSerializer.Serialize(value), passphrase);
            }
        }
        
        protected async Task SetRaw(string key, string value, string? passphrase = null)
        {
            if (!string.IsNullOrEmpty(passphrase))
            {
                value = DataEncryptor.Encrypt(value, passphrase);
            }
            await _jsRuntime.InvokeVoidAsync($"{jsNamespace}.setItem", key, value);
        }
    }
}