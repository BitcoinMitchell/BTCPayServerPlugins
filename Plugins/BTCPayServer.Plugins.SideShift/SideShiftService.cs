using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.SideShift
{
    public class SideShiftService
    {
        private readonly ISettingsRepository _settingsRepository;
        private readonly IMemoryCache _memoryCache;
        private readonly IStoreRepository _storeRepository;
        private readonly IHttpClientFactory _httpClientFactory;

        public SideShiftService(ISettingsRepository settingsRepository, IMemoryCache memoryCache, IStoreRepository storeRepository, IHttpClientFactory httpClientFactory)
        {
            _settingsRepository = settingsRepository;
            _memoryCache = memoryCache;
            _storeRepository = storeRepository;
            _httpClientFactory = httpClientFactory;
        }
        
        public async Task<SideShiftSettings> GetSideShiftForStore(string storeId)
        {
            var k = $"{nameof(SideShiftSettings)}_{storeId}";
            return await _memoryCache.GetOrCreateAsync(k, async _ =>
            {
                var res = await _storeRepository.GetSettingAsync<SideShiftSettings>(storeId,
                    nameof(SideShiftSettings));
                if (res is not null) return res;
                res = await _settingsRepository.GetSettingAsync<SideShiftSettings>(k);

                if (res is not null)
                {
                    await SetSideShiftForStore(storeId, res);
                }

                await _settingsRepository.UpdateSetting<SideShiftSettings>(null, k);
                return res;
            });
        }

        public async Task SetSideShiftForStore(string storeId, SideShiftSettings SideShiftSettings)
        {
            var k = $"{nameof(SideShiftSettings)}_{storeId}";
            await _storeRepository.UpdateSetting(storeId, nameof(SideShiftSettings), SideShiftSettings);
            _memoryCache.Set(k, SideShiftSettings);
        }
        
        public async Task<List<SideshiftSettleCoin>> GetSettleCoins()
        {
            return  await _memoryCache.GetOrCreateAsync<List<SideshiftSettleCoin>>("sideshift-coins", async entry =>
            {
                var client = _httpClientFactory.CreateClient("sideshift");
                var request = new HttpRequestMessage(HttpMethod.Get, "https://sideshift.ai/api/v2/coins");
                var response = await client.SendAsync(request);
                var result = new List<SideshiftSettleCoin>();
                if (!response.IsSuccessStatusCode)
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
                    return result;
                }
                var coins = await response.Content.ReadAsStringAsync().ContinueWith(t => JsonConvert.DeserializeObject<List<SideShiftAvailableCoin>>(t.Result));

                coins.ForEach(coin =>
                {
                    Array.ForEach (coin.networks,network =>
                    {
                        if(coin.settleOffline.Type == JTokenType.Boolean && coin.settleOffline.Value<bool>())
                            return;
                        if (coin.settleOffline is JArray settleOfflineArray &&
                            settleOfflineArray.Any(v => v.Value<string>() == network))
                        {
                            return;
                        }
                        
                        var coinType = CoinType.Both;
                        if (coin.fixedOnly.Type == JTokenType.Boolean && coin.fixedOnly.Value<bool>())
                        {
                            coinType = CoinType.FixedOnly;
                        }
                        else if (coin.fixedOnly is JArray fixedOnlyArray &&
                                 fixedOnlyArray.Any(v => v.Value<string>() == network))
                        {
                            coinType = CoinType.FixedOnly;
                        }
                        else if (coin.variableOnly.Type == JTokenType.Boolean && coin.variableOnly.Value<bool>())
                        {
                            coinType = CoinType.VariableOnly;
                        }
                        else if (coin.variableOnly is JArray variableOnlyArray &&
                                 variableOnlyArray.Any(v => v.Value<string>() == network))
                        {
                            coinType = CoinType.VariableOnly;
                        }

                        result.Add(new SideshiftSettleCoin()
                        {
                            Id = $"{coin.coin}_{network}",
                            CryptoCode = coin.coin,
                            Network = network,
                            DisplayName = coin.name,
                            HasMemo = coin.hasMemo,
                            Type = coinType,
                        });
                    });
                });
                
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                entry.Value = result; 
                return entry.Value as List<SideshiftSettleCoin>;
            });
            
            
        }

        public enum CoinType
        {
            FixedOnly,
            VariableOnly,
            Both
        }

        public class SideshiftSettleCoin:SideshiftDepositCoin
        {
            public bool HasMemo { get; set; }
        }
        public class SideshiftDepositCoin
        {
            public string DisplayName { get; set; }
            public string Id { get; set; }
            public CoinType Type { get; set; }
            public string CryptoCode { get; set; }
            public string Network { get; set; }

            public override string ToString()
            {
                return $"{DisplayName} {(DisplayName.Equals(Network, StringComparison.InvariantCultureIgnoreCase)? string.Empty: $"({Network})")}";
            }
        }
        public async Task<List<SideshiftDepositCoin>> GetDepositOptions()
        {
            return (List<SideshiftDepositCoin>) await _memoryCache.GetOrCreateAsync("sideshift-deposit", async entry =>
            {
                var client = _httpClientFactory.CreateClient("sideshift");
                var request = new HttpRequestMessage(HttpMethod.Get, "https://sideshift.ai/api/v1/facts");
                var response = await client.SendAsync(request); 
                var result = new List<SideshiftDepositCoin>();
                if (!response.IsSuccessStatusCode)
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
                    return result;
                }
                var coins = await response.Content.ReadAsStringAsync().ContinueWith(t => JsonConvert.DeserializeObject<JObject>(t.Result));
               
                foreach (var asset in coins["depositMethods"].Children<JProperty>())
                {
                    if (asset.Value["enabled"].Value<bool>() is not true)
                    {
                        continue;
                    }
                    var id = asset.Name;
                    var displayName = asset.Value["displayName"].Value<string>();
                   var coinType = asset.Value["fixedOnly"].Value<bool>() ? CoinType.FixedOnly : asset.Value["variableOnly"].Value<bool>()? CoinType.VariableOnly : CoinType.Both;
                   var network = asset.Value["network"].Value<string>();
                   var cryptoCode = asset.Value["asset"].Value<string>();
                   result.Add(new SideshiftDepositCoin()
                   {
                       Id = id,
                       DisplayName = displayName,
                       Type = coinType,
                       Network = network,
                       CryptoCode = cryptoCode,
                   });
                }
                
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                entry.Value = result;
                return entry.Value;
            });
            
            
        }
        
        
        
    }
}

