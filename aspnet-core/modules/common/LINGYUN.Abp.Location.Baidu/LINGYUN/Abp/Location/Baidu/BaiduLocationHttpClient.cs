﻿using LINGYUN.Abp.Location.Baidu.Response;
using LINGYUN.Abp.Location.Baidu.Utils;
using LINYUN.Abp.Location.Baidu.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Json;
using Volo.Abp.Threading;

namespace LINGYUN.Abp.Location.Baidu
{
    public class BaiduLocationHttpClient : ITransientDependency
    {
        protected BaiduLocationOptions Options { get; }
        protected IJsonSerializer JsonSerializer { get; }
        protected IServiceProvider ServiceProvider { get; }
        protected IHttpClientFactory HttpClientFactory { get; }
        protected ICancellationTokenProvider CancellationTokenProvider { get; }

        public BaiduLocationHttpClient(
            IOptions<BaiduLocationOptions> options,
            IJsonSerializer jsonSerializer,
            IServiceProvider serviceProvider,
            IHttpClientFactory httpClientFactory,
            ICancellationTokenProvider cancellationTokenProvider)
        {
            Options = options.Value;
            JsonSerializer = jsonSerializer;
            ServiceProvider = serviceProvider;
            HttpClientFactory = httpClientFactory;
            CancellationTokenProvider = cancellationTokenProvider;
        }

        public virtual async Task<GecodeLocation> GeocodeAsync(string address, string city = null)
        {
            var requestParamters = new Dictionary<string, string>
            {
                { "address", address },
                { "ak", Options.AccessKey },
                { "output", Options.Output },
                { "ret_coordtype", Options.ReturnCoordType }
            };
            if (!city.IsNullOrWhiteSpace())
            {
                requestParamters.Add("city", city);
            }
            var baiduMapUrl = "http://api.map.baidu.com";
            var baiduMapPath = "/geocoding/v3";
            if (Options.CaculateAKSN)
            {
                // TODO: 百度的文档不明不白,sn的算法在遇到特殊字符会验证失败,有待完善
                var sn = BaiduAKSNCaculater.CaculateAKSN(Options.AccessSecret, baiduMapPath, requestParamters);
                requestParamters.Add("sn", sn);
            }
            var requestUrl = BuildRequestUrl(baiduMapUrl, baiduMapPath, requestParamters);
            var responseContent = await MakeRequestAndGetResultAsync(requestUrl);
            var baiduLocationResponse = JsonSerializer.Deserialize<BaiduGeocodeResponse>(responseContent);
            if (!baiduLocationResponse.IsSuccess())
            {
                var localizerFactory = ServiceProvider.GetRequiredService<IStringLocalizerFactory>();
                var localizerErrorMessage = baiduLocationResponse.GetErrorMessage().Localize(localizerFactory);
                var localizerErrorDescription = baiduLocationResponse.GetErrorMessage().Localize(localizerFactory);
                var localizer = ServiceProvider.GetRequiredService<IStringLocalizer<BaiduLocationResource>>();
                localizerErrorMessage = localizer["ResolveLocationFailed", localizerErrorMessage, localizerErrorDescription];
                if (Options.VisableErrorToClient)
                {
                    throw new UserFriendlyException(localizerErrorMessage);
                }
                throw new AbpException($"Resolution address failed:{localizerErrorMessage}!");
            }
            var location = new GecodeLocation
            {
                Confidence = baiduLocationResponse.Result.Confidence,
                Latitude = baiduLocationResponse.Result.Location.Lat,
                Longitude = baiduLocationResponse.Result.Location.Lng,
                Level = baiduLocationResponse.Result.Level
            };
            location.AddAdditional("BaiduLocation", baiduLocationResponse.Result);

            return location;
        }

        public virtual async Task<ReGeocodeLocation> ReGeocodeAsync(double lat, double lng, int radius = 1000)
        {
            var requestParamters = new Dictionary<string, string>
            {
                { "ak", Options.AccessKey },
                { "output", Options.Output },
                { "radius", radius.ToString() },
                { "language", Options.Language },
                { "coordtype", Options.CoordType },
                { "extensions_poi", Options.ExtensionsPoi },
                { "ret_coordtype", Options.ReturnCoordType },
                { "location", string.Format("{0},{1}", lat, lng) },
                { "extensions_road", Options.ExtensionsRoad ? "true" : "false" },
                { "extensions_town", Options.ExtensionsTown ? "true" : "false" },
            };
            var baiduMapUrl = "http://api.map.baidu.com";
            var baiduMapPath = "/reverse_geocoding/v3";
            if (Options.CaculateAKSN)
            {
                // TODO: 百度的文档不明不白,sn的算法在遇到特殊字符会验证失败,有待完善
                var sn = BaiduAKSNCaculater.CaculateAKSN(Options.AccessSecret, baiduMapPath, requestParamters);
                requestParamters.Add("sn", sn);
            }
            requestParamters["location"] = string.Format("{0}%2C{1}", lat, lng);
            var requestUrl = BuildRequestUrl(baiduMapUrl, baiduMapPath, requestParamters);
            var responseContent = await MakeRequestAndGetResultAsync(requestUrl);
            var baiduLocationResponse = JsonSerializer.Deserialize<BaiduReGeocodeResponse>(responseContent);
            if (!baiduLocationResponse.IsSuccess())
            {
                var localizerFactory = ServiceProvider.GetRequiredService<IStringLocalizerFactory>();
                var localizerErrorMessage = baiduLocationResponse.GetErrorMessage().Localize(localizerFactory);
                var localizerErrorDescription = baiduLocationResponse.GetErrorDescription().Localize(localizerFactory);
                var localizer = ServiceProvider.GetRequiredService<IStringLocalizer<BaiduLocationResource>>();
                localizerErrorMessage = localizer["ResolveLocationFailed", localizerErrorMessage, localizerErrorDescription];
                if (Options.VisableErrorToClient)
                {
                    throw new UserFriendlyException(localizerErrorMessage);
                }
                throw new AbpException($"Resolution address failed:{localizerErrorMessage}!");
            }
            var location = new ReGeocodeLocation
            {
                Street = baiduLocationResponse.Result.AddressComponent.Street,
                AdCode = baiduLocationResponse.Result.AddressComponent.AdCode.ToString(),
                Address = baiduLocationResponse.Result.Address,
                City = baiduLocationResponse.Result.AddressComponent.City,
                Country = baiduLocationResponse.Result.AddressComponent.Country,
                District = baiduLocationResponse.Result.AddressComponent.District,
                Number = baiduLocationResponse.Result.AddressComponent.StreetNumber,
                Province = baiduLocationResponse.Result.AddressComponent.Province,
                Town = baiduLocationResponse.Result.AddressComponent.Town,
                Pois = baiduLocationResponse.Result.Pois.Select(p => new Poi
                {
                    Address = p.Address,
                    Name = p.Name,
                    Tag = p.Tag,
                    Type = p.PoiType
                }).ToList(),
                Roads = baiduLocationResponse.Result.Roads.Select(r => new Road
                {
                    Name = r.Name
                }).ToList()
            };
            location.AddAdditional("BaiduLocation", baiduLocationResponse.Result);

            return location;
        }

        protected virtual async Task<string> MakeRequestAndGetResultAsync(string url)
        {
            var client = HttpClientFactory.CreateClient(BaiduLocationHttpConsts.HttpClientName);
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);

            var response = await client.SendAsync(requestMessage, GetCancellationToken());
            if (!response.IsSuccessStatusCode)
            {
                throw new AbpException($"Baidu http request service returns error! HttpStatusCode: {response.StatusCode}, ReasonPhrase: {response.ReasonPhrase}");
            }
            var resultContent = await response.Content.ReadAsStringAsync();

            return resultContent;
        }

        protected virtual CancellationToken GetCancellationToken()
        {
            return CancellationTokenProvider.Token;
        }

        protected virtual string BuildRequestUrl(string uri, string path, IDictionary<string, string> paramters)
        {
            var requestUrlBuilder = new StringBuilder(128);
            requestUrlBuilder.Append(uri);
            requestUrlBuilder.Append(path).Append("?");
            foreach (var paramter in paramters)
            {
                requestUrlBuilder.AppendFormat("{0}={1}", paramter.Key, paramter.Value);
                requestUrlBuilder.Append("&");
            }
            requestUrlBuilder.Remove(requestUrlBuilder.Length - 1, 1);
            return requestUrlBuilder.ToString();
        }
    }
}
