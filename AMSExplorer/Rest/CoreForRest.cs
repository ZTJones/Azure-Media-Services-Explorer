﻿//----------------------------------------------------------------------------------------------
//    Copyright 2020 Microsoft Corporation
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
//---------------------------------------------------------------------------------------------


using Microsoft.Azure.Management.Storage.Models;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Globalization;
using System.Linq;
using System.Net.Http;

namespace AMSExplorer.Rest
{
    public partial class AmsClientRest
    {
        private readonly AMSClientV3 _amsClient;

        public AmsClientRest(AMSClientV3 amsClient)
        {
            _amsClient = amsClient;
        }

        private string GenerateApiUrl(string url, string objectName)
        {
            return _amsClient.environment.ArmEndpoint
                                       + string.Format(url,
                                                          _amsClient.credentialsEntry.AzureSubscriptionId,
                                                          _amsClient.credentialsEntry.ResourceGroup,
                                                          _amsClient.credentialsEntry.AccountName,
                                                          objectName
                                                  );
        }

        private string GetToken()
        {
           return _amsClient.accessToken != null ? _amsClient.accessToken.AccessToken :
                TokenCache.DefaultShared.ReadItems()
                    .Where(t => t.ClientId == _amsClient.credentialsEntry.ADSPClientId)
                    .OrderByDescending(t => t.ExpiresOn)
                    .First().AccessToken;
        }

        private HttpClient GetHttpClient()
        {
            HttpClient client = _amsClient.AMSclient.HttpClient;
            client.DefaultRequestHeaders.Remove("Authorization");
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + GetToken());
            return client;
        }


       
    }

    internal static class ConverterLE
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            Converters =
            {
                new IsoDateTimeConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }
            },
        };
    }
}