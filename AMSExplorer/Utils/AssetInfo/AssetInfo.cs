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

using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Azure.Management.ResourceManager;
using Microsoft.Azure.Management.Storage;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using System.Xml.Linq;

namespace AMSExplorer
{
    public class AssetInfo
    {
        private readonly List<Asset> SelectedAssetsV3;
        private readonly AMSClientV3 _amsClient;
        public const string Type_Empty = "(empty)";
        public const string Type_Workflow = "Workflow";
        public const string Type_Single = "Single Bitrate MP4";
        public const string Type_Multi = "Multi Bitrate MP4";
        public const string Type_Extension_No_Client_Manifest = " with no ismc";
        public const string Type_Smooth = "Smooth Streaming";
        public const string Type_LiveArchive = "Live Archive";
        public const string Type_Fragmented = "Pre-fragmented";
        public const string Type_AMSHLS = "Media Services HLS";
        public const string Type_Thumbnails = "Thumbnails";
        public const string Type_Unknown = "Unknown";
        public const string _prog_down_https_SAS = "Progressive Download URLs (SAS)";
        public const string _prog_down_http_streaming = "Progressive Download URLs (SE)";
        public const string _hls_cmaf = "HLS CMAF URL";
        public const string _hls_v4 = "HLS v4 URL";
        public const string _hls_v3 = "HLS v3 URL";
        public const string _dash_csf = "MPEG-DASH CSF URL";
        public const string _dash_cmaf = "MPEG-DASH CMAF URL";
        public const string _smooth = "Smooth Streaming URL";
        public const string _smooth_legacy = "Smooth Streaming (legacy) URL";
        public const string _hls = "HLS URL";

        public const string format_smooth_legacy = "fmp4-v20";
        public const string format_hls_v4 = "m3u8-aapl";
        public const string format_hls_v3 = "m3u8-aapl-v3";
        public const string format_hls_cmaf = "m3u8-cmaf";
        public const string format_dash_csf = "mpd-time-csf";
        public const string format_dash_cmaf = "mpd-time-cmaf";

        private const string format_url = "format={0}";
        private const string filter_url = "filter={0}";
        private const string audioTrack_url = "audioTrack={0}";

        private const string ManifestFileExtension = ".ism";


        public AssetInfo(Asset myAsset, AMSClientV3 amsClient)
        {
            SelectedAssetsV3 = new List<Asset>() { myAsset };
            _amsClient = amsClient;
        }

        public AssetInfo(List<Asset> mySelectedAssets, AMSClientV3 amsClient)
        {
            SelectedAssetsV3 = mySelectedAssets;
            _amsClient = amsClient;
        }
        public AssetInfo(Asset asset)
        {
            SelectedAssetsV3 = new List<Asset>() { asset };
        }


        public static async Task<StreamingLocator> CreateTemporaryOnDemandLocatorAsync(Asset asset, AMSClientV3 _amsClientV3)
        {
            StreamingLocator tempLocator = null;
            await _amsClientV3.RefreshTokenIfNeededAsync();

            try
            {
                string streamingLocatorName = "templocator-" + Program.GetUniqueness();

                tempLocator = new StreamingLocator(
                    assetName: asset.Name,
                    streamingPolicyName: PredefinedStreamingPolicy.ClearStreamingOnly,
                    streamingLocatorId: null,
                    startTime: DateTime.UtcNow.AddMinutes(-5),
                    endTime: DateTime.UtcNow.AddHours(1)
                    );


                tempLocator = await _amsClientV3.AMSclient.StreamingLocators.CreateAsync(_amsClientV3.credentialsEntry.ResourceGroup, _amsClientV3.credentialsEntry.AccountName, streamingLocatorName, tempLocator);
            }

            catch
            {
                throw;
            }

            return tempLocator;
        }

        public static async Task DeleteStreamingLocatorAsync(AMSClientV3 _amsClientV3, string streamingLocatorName)
        {
            await _amsClientV3.RefreshTokenIfNeededAsync();

            try
            {
                await _amsClientV3.AMSclient.StreamingLocators.DeleteAsync(_amsClientV3.credentialsEntry.ResourceGroup, _amsClientV3.credentialsEntry.AccountName, streamingLocatorName);
            }
            catch
            {
                throw;
            }
        }

        public static async Task<Uri> GetValidOnDemandSmoothURIAsync(Asset asset, AMSClientV3 _amsClient, string useThisLocatorName = null)
        {
            await _amsClient.RefreshTokenIfNeededAsync();

            IList<AssetStreamingLocator> locators = (await _amsClient.AMSclient.Assets.ListStreamingLocatorsAsync(_amsClient.credentialsEntry.ResourceGroup, _amsClient.credentialsEntry.AccountName, asset.Name)).StreamingLocators;

            Microsoft.Rest.Azure.IPage<StreamingEndpoint> ses = await _amsClient.AMSclient.StreamingEndpoints.ListAsync(_amsClient.credentialsEntry.ResourceGroup, _amsClient.credentialsEntry.AccountName);

            StreamingEndpoint runningSes = ses.Where(s => s.ResourceState == StreamingEndpointResourceState.Running).FirstOrDefault();
            if (runningSes == null)
            {
                runningSes = ses.FirstOrDefault();
            }

            if (locators.Count > 0 && runningSes != null)
            {
                string locatorName = useThisLocatorName ?? locators.First().Name;
                IList<StreamingPath> streamingPaths = (await _amsClient.AMSclient.StreamingLocators.ListPathsAsync(_amsClient.credentialsEntry.ResourceGroup, _amsClient.credentialsEntry.AccountName, locatorName)).StreamingPaths;
                IEnumerable<StreamingPath> smoothPath = streamingPaths.Where(p => p.StreamingProtocol == StreamingPolicyStreamingProtocol.SmoothStreaming);
                if (smoothPath.Count() > 0)
                {
                    UriBuilder uribuilder = new UriBuilder
                    {
                        Host = runningSes.HostName,
                        Path = smoothPath.FirstOrDefault().Paths.FirstOrDefault()
                    };
                    return uribuilder.Uri;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }


     


        public static async Task<AssetStreamingLocator> IsThereALocatorValidAsync(Asset asset, AMSClientV3 amsClient)
        {
            if (asset == null) return null;

            await amsClient.RefreshTokenIfNeededAsync();
            IList<AssetStreamingLocator> locators = (await amsClient.AMSclient.Assets.ListStreamingLocatorsAsync(amsClient.credentialsEntry.ResourceGroup, amsClient.credentialsEntry.AccountName, asset.Name))
                                                    .StreamingLocators;

            if (locators.Count > 0)
            {
                AssetStreamingLocator LocatorQuery = locators.Where(l => ((l.StartTime < DateTime.UtcNow) || (l.StartTime == null)) && (l.EndTime > DateTime.UtcNow)).FirstOrDefault();
                if (LocatorQuery != null)
                {
                    return LocatorQuery;
                }
            }
            return null;
        }

        public static string GetSmoothLegacy(string smooth_uri)
        {
            return string.Format("{0}(format={1})", smooth_uri, format_smooth_legacy);
        }
        public static string AddFilterToUrlString(string urlstr, string filter)
        {
            // add a filter
            if (filter != null)
            {
                return AddParameterToUrlString(urlstr, string.Format(AssetInfo.filter_url, filter));
            }
            else
            {
                return urlstr;
            }
        }

        public static string AddAudioTrackToUrlString(string urlstr, string trackname)
        {
            // add a track name
            if (trackname != null)
            {
                return AddParameterToUrlString(urlstr, string.Format(AssetInfo.audioTrack_url, trackname));
            }
            else
            {
                return urlstr;
            }
        }

        public static string AddHLSNoAudioOnlyModeToUrlString(string urlstr)
        {
            return AddParameterToUrlString(urlstr, "audio-only=false");
        }

        public static string AddProtocolFormatInUrlString(string urlstr, AMSOutputProtocols protocol = AMSOutputProtocols.NotSpecified)
        {
            switch (protocol)
            {
                case AMSOutputProtocols.DashCsf:
                    return AddParameterToUrlString(urlstr, string.Format(AssetInfo.format_url, AssetInfo.format_dash_csf)) + Constants.mpd;

                case AMSOutputProtocols.DashCmaf:
                    return AddParameterToUrlString(urlstr, string.Format(AssetInfo.format_url, AssetInfo.format_dash_cmaf)) + Constants.mpd;

                case AMSOutputProtocols.HLSv3:
                    return AddParameterToUrlString(urlstr, string.Format(AssetInfo.format_url, AssetInfo.format_hls_v3)) + Constants.m3u8;

                case AMSOutputProtocols.HLSv4:
                    return AddParameterToUrlString(urlstr, string.Format(AssetInfo.format_url, AssetInfo.format_hls_v4)) + Constants.m3u8;

                case AMSOutputProtocols.HLSCmaf:
                    return AddParameterToUrlString(urlstr, string.Format(AssetInfo.format_url, AssetInfo.format_hls_cmaf)) + Constants.m3u8;

                case AMSOutputProtocols.SmoothLegacy:
                    return AddParameterToUrlString(urlstr, string.Format(AssetInfo.format_url, AssetInfo.format_smooth_legacy));

                case AMSOutputProtocols.NotSpecified:
                default:
                    return urlstr;
            }
        }

        public static string AddParameterToUrlString(string urlstr, string parameter)
        {
            // add a parameter (like "format=mpd-time-csf" or "filter=myfilter" or "audioTrack=name to urlstr

            const string querystr = "/manifest(";

            // let's remove temporary the extension
            string streamExtension = string.Empty;
            if (urlstr.EndsWith(Constants.mpd))
            {
                streamExtension = Constants.mpd;
                urlstr = urlstr.Substring(0, urlstr.Length - Constants.mpd.Length);
            }
            else if (urlstr.EndsWith(Constants.m3u8))
            {
                streamExtension = Constants.m3u8;
                urlstr = urlstr.Substring(0, urlstr.Length - Constants.m3u8.Length);
            }

            if (urlstr.Contains(querystr)) // there is already a parameter
            {
                int pos = urlstr.IndexOf(querystr, 0);
                urlstr = urlstr.Substring(0, pos + 10) + parameter + "," + urlstr.Substring(pos + 10);
            }
            else
            {
                urlstr += string.Format("({0})", parameter);
            }

            return urlstr + streamExtension; // we restore the extension
        }

        public static Uri RW(string path, StreamingEndpoint se, string filters = null, bool https = false, string customHostName = null, AMSOutputProtocols protocol = AMSOutputProtocols.NotSpecified, string audiotrackname = null, bool HLSNoAudioOnly = false)
        {
            return RW(new Uri("https://" + se.HostName + path), se, filters, https, customHostName, protocol, audiotrackname, HLSNoAudioOnly);
        }

        // return the URL with hostname from streaming endpoint
        public static Uri RW(Uri url, StreamingEndpoint se = null, string filters = null, bool https = false, string customHostName = null, AMSOutputProtocols protocol = AMSOutputProtocols.NotSpecified, string audiotrackname = null, bool HLSNoAudioOnly = false)
        {
            if (url != null)
            {
                string path = AddFilterToUrlString(url.AbsolutePath, filters);
                path = AddProtocolFormatInUrlString(path, protocol);

                if (protocol == AMSOutputProtocols.HLSv3)
                {
                    path = AddAudioTrackToUrlString(path, audiotrackname);
                    if (HLSNoAudioOnly)
                    {
                        path = AddHLSNoAudioOnlyModeToUrlString(path);
                    }
                }

                string hostname = null;
                if (customHostName != null)
                {
                    hostname = customHostName;
                }
                else if (se != null)
                {
                    hostname = se.HostName;
                }

                UriBuilder urib = new UriBuilder()
                {
                    Host = hostname ?? url.Host,
                    Scheme = https ? "https://" : "http://",
                    Path = path,
                };
                return urib.Uri;
            }
            else
            {
                return null;
            }
        }


        public static string RW(string path, StreamingEndpoint se, string filter = null, bool https = false, string customhostname = null, AMSOutputProtocols protocol = AMSOutputProtocols.NotSpecified, string audiotrackname = null)
        {
            return RW(new Uri(path), se, filter, https, customhostname, protocol, audiotrackname).AbsoluteUri;
        }



        public static PublishStatus GetPublishedStatusForLocator(AssetStreamingLocator Locator)
        {
            PublishStatus LocPubStatus;
            if (!(Locator.EndTime < DateTime.UtcNow))
            {// not in the past
             // if  locator is not valid today but will be in the future
                if (Locator.StartTime != null)
                {
                    LocPubStatus = (Locator.StartTime > DateTime.UtcNow) ? PublishStatus.PublishedFuture : PublishStatus.PublishedActive;
                }
                else
                {
                    LocPubStatus = PublishStatus.PublishedActive;
                }
            }
            else      // if locator is in the past
            {
                LocPubStatus = PublishStatus.PublishedExpired;
            }
            return LocPubStatus;
        }

        public static PublishStatus GetPublishedStatusForLocator(StreamingLocator Locator)
        {
            PublishStatus LocPubStatus;
            if (!(Locator.EndTime < DateTime.UtcNow))
            {// not in the past
             // if  locator is not valid today but will be in the future
                if (Locator.StartTime != null)
                {
                    LocPubStatus = (Locator.StartTime > DateTime.UtcNow) ? PublishStatus.PublishedFuture : PublishStatus.PublishedActive;
                }
                else
                {
                    LocPubStatus = PublishStatus.PublishedActive;
                }
            }
            else      // if locator is in the past
            {
                LocPubStatus = PublishStatus.PublishedExpired;
            }
            return LocPubStatus;
        }

        public static TimeSpan ReturnTimeSpanOnGOP(ManifestTimingData data, TimeSpan ts)
        {
            TimeSpan response = ts;
            ulong timestamp = (ulong)(ts.TotalSeconds * data.TimeScale);

            int i = 0;
            foreach (ulong t in data.TimestampList)
            {
                if (t < timestamp && i < (data.TimestampList.Count - 1) && timestamp < data.TimestampList[i + 1])
                {
                    response = TimeSpan.FromSeconds(t / (double)data.TimeScale);
                    break;
                }
                i++;
            }
            return response;
        }


        public static async Task<XDocument> TryToGetClientManifestContentAsABlobAsync(Asset asset, AMSClientV3 _amsClient)
        {
            // get the manifest
            ListContainerSasInput input = new ListContainerSasInput()
            {
                Permissions = AssetContainerPermission.Read,
                ExpiryTime = DateTime.Now.AddMinutes(5).ToUniversalTime()
            };
            await _amsClient.RefreshTokenIfNeededAsync();

            AssetContainerSas responseSas = await _amsClient.AMSclient.Assets.ListContainerSasAsync(_amsClient.credentialsEntry.ResourceGroup, _amsClient.credentialsEntry.AccountName, asset.Name, input.Permissions, input.ExpiryTime);

            string uploadSasUrl = responseSas.AssetContainerSasUrls.First();

            Uri sasUri = new Uri(uploadSasUrl);
            CloudBlobContainer container = new CloudBlobContainer(sasUri);


            BlobContinuationToken continuationToken = null;
            List<CloudBlockBlob> blobs = new List<CloudBlockBlob>();

            do
            {
                BlobResultSegment segment = await container.ListBlobsSegmentedAsync(null, false, BlobListingDetails.Metadata, null, continuationToken, null, null);
                blobs.AddRange(segment.Results.Where(blob => blob.GetType() == typeof(CloudBlockBlob)).Select(b => b as CloudBlockBlob));

                continuationToken = segment.ContinuationToken;
            }
            while (continuationToken != null);
            IEnumerable<CloudBlockBlob> ismc = blobs.Where(b => b.Name.EndsWith(".ismc", StringComparison.OrdinalIgnoreCase));

            if (ismc.Count() == 0)
            {
                throw new Exception("No ISMC file in asset.");
            }

            string content = await ismc.First().DownloadTextAsync();

            return XDocument.Parse(content);
        }

        public static async Task<XDocument> TryToGetClientManifestContentUsingStreamingLocatorAsync(Asset asset, AMSClientV3 _amsClient, string preferredLocatorName = null)
        {
            Uri myuri = await GetValidOnDemandSmoothURIAsync(asset, _amsClient, preferredLocatorName);

            if (myuri == null)
            {
                myuri = await GetValidOnDemandSmoothURIAsync(asset, _amsClient);
            }
            if (myuri != null)
            {
                return XDocument.Load(myuri.ToString());
            }
            else
            {
                throw new Exception("Streaming locator is null");
            }
        }

        /// <summary>
        /// Parse the manifest data.
        /// It is recommended to call TryToGetClientManifestContentAsABlobAsync and TryToGetClientManifestContentUsingStreamingLocatorAsync to get the content
        /// </summary>
        /// <param name="manifest"></param>
        /// <returns></returns>
        public static ManifestTimingData GetManifestTimingData(XDocument manifest)
        {

            if (manifest == null) throw new ArgumentNullException();

            ManifestTimingData response = new ManifestTimingData() { IsLive = false, Error = false, TimestampOffset = 0, TimestampList = new List<ulong>(), DiscontinuityDetected = false };

            try
            {
                XElement smoothmedia = manifest.Element("SmoothStreamingMedia");
                IEnumerable<XElement> videotrack = smoothmedia.Elements("StreamIndex").Where(a => a.Attribute("Type").Value == "video");

                // TIMESCALE
                long? rootTimeScaleFromManifest = smoothmedia.Attribute("TimeScale").Value != null ? long.Parse(smoothmedia.Attribute("TimeScale").Value) : (long?)null;

                long? videoTimeScaleFromManifest = null;
                if (videotrack.FirstOrDefault().Attribute("TimeScale") != null) // there is timescale value in the video track. Let's take this one.
                {
                    videoTimeScaleFromManifest = long.Parse(videotrack.FirstOrDefault().Attribute("TimeScale").Value);
                }

                // by default, we use the timescale of the video track, except if there is no timescale. In that case, let's take the root one.
                long timescaleVideo = (long)(videoTimeScaleFromManifest ?? rootTimeScaleFromManifest);
                response.TimeScale = timescaleVideo;

                // DURATION
                string durationFromManifest = smoothmedia.Attribute("Duration").Value;
                ulong? overallDuration = null;
                if (durationFromManifest != null && rootTimeScaleFromManifest != null) // there is a duration value in the root (and a timescale). Let's take this one.
                {
                    var ratio = (double)rootTimeScaleFromManifest / (double)videoTimeScaleFromManifest;
                    overallDuration = (ulong?)(ulong.Parse(durationFromManifest) / ratio); // value with the timescale of the video track
                }

                // Timestamp offset
                if (videotrack.FirstOrDefault().Element("c").Attribute("t") != null)
                {
                    response.TimestampOffset = ulong.Parse(videotrack.FirstOrDefault().Element("c").Attribute("t").Value);
                }
                else
                {
                    response.TimestampOffset = 0; // no timestamp, so it should be 0
                }

                ulong totalDuration = 0;
                ulong durationPreviousChunk = 0;
                ulong durationChunk;
                int repeatChunk;
                foreach (XElement chunk in videotrack.Elements("c"))
                {
                    durationChunk = chunk.Attribute("d") != null ? ulong.Parse(chunk.Attribute("d").Value) : 0;
                    repeatChunk = chunk.Attribute("r") != null ? int.Parse(chunk.Attribute("r").Value) : 1;

                    if (chunk.Attribute("t") != null)
                    {
                        ulong tvalue = ulong.Parse(chunk.Attribute("t").Value);
                        response.TimestampList.Add(tvalue);
                        if (tvalue != response.TimestampOffset)
                        {
                            totalDuration = tvalue - response.TimestampOffset; // Discountinuity ? We calculate the duration from the offset
                            response.DiscontinuityDetected = true; // let's flag it
                        }
                    }
                    else
                    {
                        response.TimestampList.Add(response.TimestampList[response.TimestampList.Count() - 1] + durationPreviousChunk);
                    }

                    totalDuration += durationChunk * (ulong)repeatChunk;

                    for (int i = 1; i < repeatChunk; i++)
                    {
                        response.TimestampList.Add(response.TimestampList[response.TimestampList.Count() - 1] + durationChunk);
                    }

                    durationPreviousChunk = durationChunk;
                }
                response.TimestampEndLastChunk = response.TimestampList[response.TimestampList.Count() - 1] + durationPreviousChunk;

                if (smoothmedia.Attribute("IsLive") != null && smoothmedia.Attribute("IsLive").Value == "TRUE")
                { // Live asset.... No duration to read or it is always zero (but we can read scaling and compute duration)
                    response.IsLive = true;
                    response.AssetDuration = TimeSpan.FromSeconds(totalDuration / ((double)timescaleVideo));
                }
                else
                {
                    if (overallDuration != null & overallDuration > 0) // let's trust the duration property in the manifest
                    {
                        response.AssetDuration = TimeSpan.FromSeconds((ulong)overallDuration / ((double)timescaleVideo));

                    }
                    else // no trust
                    {
                        response.AssetDuration = TimeSpan.FromSeconds(totalDuration / ((double)timescaleVideo));
                    }
                }
            }
            catch
            {
                response.Error = true;
            }
            return response;
        }


        public class ManifestSegmentData
        {
            public ulong timestamp;
            public bool calculated; // it means the timestamp has been calculated from previous
            public bool timestamp_mismatch; // if there is a mismatch
        }

        public class ManifestSegmentsResponse
        {
            public List<ManifestSegmentData> videoSegments;
            public List<int> videoBitrates;
            public string videoName;
            public ManifestSegmentData[][] audioSegments;
            public int[][] audioBitrates;
            public string[] audioName;

            public ManifestSegmentsResponse()
            {
                videoSegments = new List<ManifestSegmentData>();
                videoBitrates = new List<int>();
            }
        }

        /*
        static public ManifestSegmentsResponse GetManifestSegmentsList(IAsset asset)
        // Parse the manifest and get data from it
        {
            ManifestSegmentsResponse response = new ManifestSegmentsResponse();

            try
            {
                ILocator mytemplocator = null;
                Uri myuri = AssetInfo.GetValidOnDemandURI(asset);
                if (myuri == null)
                {
                    mytemplocator = AssetInfo.CreatedTemporaryOnDemandLocator(asset);
                    myuri = AssetInfo.GetValidOnDemandURI(asset);
                }
                if (myuri != null)
                {
                    XDocument manifest = XDocument.Load(myuri.ToString());
                    var smoothmedia = manifest.Element("SmoothStreamingMedia");

                    ulong d = 0, r;
                    bool calc = true;
                    bool mismatch = false;
                    bool firstchunk = true;
                    ulong timeStamp = 0;

                    // video track
                    var videotrack = smoothmedia.Elements("StreamIndex").Where(a => a.Attribute("Type").Value == "video").FirstOrDefault();
                    response.videoBitrates = videotrack.Elements("QualityLevel").Select(e => int.Parse(e.Attribute("Bitrate").Value)).ToList();
                    response.videoName = videotrack.Attribute("Name").Value;

                    foreach (var chunk in videotrack.Elements("c"))
                    {
                        mismatch = false;
                        if (chunk.Attribute("t") != null)
                        {
                            var readtimeStamp = ulong.Parse(chunk.Attribute("t").Value);
                            mismatch = (!firstchunk && readtimeStamp != timeStamp);
                            timeStamp = readtimeStamp;
                            calc = false;
                            firstchunk = false;
                        }
                        else
                        {
                            calc = true;
                        }

                        d = chunk.Attribute("d") != null ? ulong.Parse(chunk.Attribute("d").Value) : 0;
                        r = chunk.Attribute("r") != null ? ulong.Parse(chunk.Attribute("r").Value) : 1;
                        for (ulong i = 0; i < r; i++)
                        {
                            response.videoSegments.Add(new ManifestSegmentData()
                            {
                                timestamp = timeStamp,
                                timestamp_mismatch = (i == 0) ? mismatch : false,
                                calculated = (i == 0) ? calc : true
                            });
                            timeStamp += d;
                        }
                    }

                    // audio track
                    var audiotracks = smoothmedia.Elements("StreamIndex").Where(a => a.Attribute("Type").Value == "audio");
                    response.audioBitrates = new int[audiotracks.Count()][];
                    response.audioSegments = new ManifestSegmentData[audiotracks.Count()][];
                    response.audioName = new string[audiotracks.Count()];


                    int a_index = 0;
                    foreach (var audiotrack in audiotracks)
                    {
                        response.audioBitrates[a_index] = audiotrack.Elements("QualityLevel").Select(e => int.Parse(e.Attribute("Bitrate").Value)).ToArray();
                        response.audioName[a_index] = audiotrack.Attribute("Name").Value;

                        var audiotracksegmentdata = new List<ManifestSegmentData>();

                        timeStamp = 0;
                        d = 0;
                        firstchunk = true;
                        foreach (var chunk in audiotrack.Elements("c"))
                        {
                            mismatch = false;
                            if (chunk.Attribute("t") != null)
                            {
                                var readtimeStamp = ulong.Parse(chunk.Attribute("t").Value);
                                mismatch = (!firstchunk && readtimeStamp != timeStamp);
                                timeStamp = readtimeStamp;
                                calc = false;
                                firstchunk = false;
                            }
                            else
                            {
                                calc = true;
                            }

                            d = chunk.Attribute("d") != null ? ulong.Parse(chunk.Attribute("d").Value) : 0;
                            r = chunk.Attribute("r") != null ? ulong.Parse(chunk.Attribute("r").Value) : 1;
                            for (ulong i = 0; i < r; i++)
                            {
                                audiotracksegmentdata.Add(new ManifestSegmentData()
                                {
                                    timestamp = timeStamp,
                                    timestamp_mismatch = (i == 0) ? mismatch : false,
                                    calculated = (i == 0) ? calc : true
                                });
                                timeStamp += d;
                            }

                        }
                        response.audioSegments[a_index] = audiotracksegmentdata.ToArray();
                        a_index++;
                    }
                }
                else
                {
                    // Error
                }
                if (mytemplocator != null) mytemplocator.Delete();
            }
            catch
            {
                // Error
            }
            return response;
        }
        */

        public static long ReturnTimestampInTicks(ulong timestamp, ulong? timescale)
        {
            double timescale2 = timescale ?? TimeSpan.TicksPerSecond;
            return (long)(timestamp * (double)TimeSpan.TicksPerSecond / timescale2);
        }


        public static async Task<AssetInfoData> GetAssetTypeAsync(string assetName, AMSClientV3 _amsClient)
        {
            ListContainerSasInput input = new ListContainerSasInput()
            {
                Permissions = AssetContainerPermission.ReadWriteDelete,
                ExpiryTime = DateTime.Now.AddHours(2).ToUniversalTime()
            };
            await _amsClient.RefreshTokenIfNeededAsync();

            string type = string.Empty;
            long size = 0;

            AssetContainerSas response = null;
            try
            {
                response = await _amsClient.AMSclient.Assets.ListContainerSasAsync(_amsClient.credentialsEntry.ResourceGroup, _amsClient.credentialsEntry.AccountName, assetName, input.Permissions, input.ExpiryTime);
            }
            catch
            {
                return null;
            }

            string uploadSasUrl = response.AssetContainerSasUrls.First();

            Uri sasUri = new Uri(uploadSasUrl);
            CloudBlobContainer container = new CloudBlobContainer(sasUri);

            BlobContinuationToken continuationToken1 = null;
            List<IListBlobItem> rootBlobs = new List<IListBlobItem>();
            do
            {
                BlobResultSegment segment = await container.ListBlobsSegmentedAsync(null, false, BlobListingDetails.Metadata, null, continuationToken1, null, null);
                continuationToken1 = segment.ContinuationToken;
                rootBlobs = segment.Results.ToList();
            }
            while (continuationToken1 != null);

            List<CloudBlockBlob> blocsc = rootBlobs.Where(b => b.GetType() == typeof(CloudBlockBlob)).Select(b => (CloudBlockBlob)b).ToList();
            List<CloudBlobDirectory> blocsdir = rootBlobs.Where(b => b.GetType() == typeof(CloudBlobDirectory)).Select(b => (CloudBlobDirectory)b).ToList();

            int number = blocsc.Count;

            CloudBlockBlob[] ismfiles = blocsc.Where(f => f.Name.EndsWith(".ism", StringComparison.OrdinalIgnoreCase)).ToArray();
            CloudBlockBlob[] ismcfiles = blocsc.Where(f => f.Name.EndsWith(".ismc", StringComparison.OrdinalIgnoreCase)).ToArray();

            // size calculation
            blocsc.ForEach(b => size += b.Properties.Length);

            // fragments in subfolders (live archive)
            foreach (CloudBlobDirectory dir in blocsdir)
            {
                CloudBlobDirectory dirRef = container.GetDirectoryReference(dir.Prefix);

                BlobContinuationToken continuationToken = null;
                List<CloudBlockBlob> subBlobs = new List<CloudBlockBlob>();
                do
                {
                    BlobResultSegment segment = await dirRef.ListBlobsSegmentedAsync(true, BlobListingDetails.Metadata, null, continuationToken, null, null);
                    continuationToken = segment.ContinuationToken;
                    subBlobs = segment.Results.Where(b => b.GetType() == typeof(CloudBlockBlob)).Select(b => (CloudBlockBlob)b).ToList();
                    subBlobs.ForEach(b => size += b.Properties.Length);
                }
                while (continuationToken != null);
            }

            CloudBlockBlob[] mp4files = blocsc.Where(f => f.Name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)).ToArray();

            if (mp4files.Count() > 0 && ismcfiles.Count() <= 1 && ismfiles.Count() == 1)  // Multi bitrate MP4
            {
                number = mp4files.Count();
                type = number == 1 ? Type_Single : Type_Multi;

                if (ismcfiles.Count() == 0) // no client manifest
                {
                    type += Type_Extension_No_Client_Manifest;
                }
            }
            else if (blocsc.Count == 0)
            {
                return new AssetInfoData() { Size = size, Type = Type_Empty };
            }
            else if (ismcfiles.Count() == 1 && ismfiles.Count() == 1 && blocsdir.Count > 0)
            {
                type = Type_LiveArchive;
                number = blocsdir.Count;
            }

            else if (blocsc.Count == 1)
            {
                number = 1;
                string ext = Path.GetExtension(blocsc.FirstOrDefault().Name.ToUpper());
                if (!string.IsNullOrEmpty(ext))
                {
                    ext = ext.Substring(1);
                }

                switch (ext)
                {
                    case "WORKFLOW":
                        type = Type_Workflow;
                        break;

                    default:
                        type = ext;
                        break;
                }
            }

            else
            {
                type = Type_Unknown;
            }

            return new AssetInfoData()
            {
                Size = size,
                Type = string.Format("{0} ({1})", type, number),
                Blobs = rootBlobs
            };
        }


        public async Task CopyStatsToClipBoardAsync()
        {
            StringBuilder SB = await GetStatsAsync();
            Clipboard.SetText(SB.ToString());
        }


        public static string FormatByteSize(long? byteCountl)
        {
            if (byteCountl.HasValue == true)
            {
                long byteCount = (long)byteCountl;
                string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
                if (byteCount == 0)
                {
                    return "0 " + suf[0];
                }

                long bytes = Math.Abs(byteCount);
                int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1000)));
                double num = Math.Round(bytes / Math.Pow(1000, place), 1);
                return (Math.Sign(byteCount) * num).ToString() + " " + suf[place];
            }
            else
            {
                return null;
            }
        }

        public static long? Inverse_FormatByteSize(string mystring)
        {
            List<UnitSize> sizes = new List<UnitSize> {
                  new UnitSize() { Unitn = "B", Mult = 1 },
                  new UnitSize(){ Unitn = "KB", Mult = 1000 },
                  new UnitSize(){ Unitn = "MB", Mult = (long)1000*1000 },
                  new UnitSize(){ Unitn = "GB", Mult = (long)1000*1000*1000 },
                  new UnitSize(){ Unitn = "TB", Mult = (long)1000*1000*1000*1000 },
                  new UnitSize(){ Unitn = "PB", Mult = (long)1000*1000*1000*1000*1000 },
                  new UnitSize(){ Unitn = "EB", Mult = (long)1000*1000*1000*1000*1000*1000 }
                  };

            if (sizes.Any(s => mystring.EndsWith(" " + s.Unitn)))
            {
                string val = mystring.Substring(0, mystring.Length - 2).Trim();
                try
                {
                    double valdouble = double.Parse(val);
                    string myunit = mystring.Substring(mystring.Length - 2, 2).Trim();
                    long mymult = sizes.Where(s => s.Unitn == myunit).FirstOrDefault().Mult;
                    return (long)(valdouble * mymult);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        public class UnitSize
        {
            public string Unitn { get; set; }
            public long Mult { get; set; }
        }


        public static AssetProtectionType GetAssetProtection(AssetStreamingLocator locator)
        {
            AssetProtectionType type = AssetProtectionType.None;

            if (locator != null)
            {
                if (locator.StreamingPolicyName == PredefinedStreamingPolicy.ClearKey.ToString())
                {
                    type = AssetProtectionType.AES;
                }
                else if (locator.StreamingPolicyName == PredefinedStreamingPolicy.MultiDrmCencStreaming.ToString())
                {
                    type = AssetProtectionType.PlayReadyAndWidevine;
                }
                else if (locator.StreamingPolicyName == PredefinedStreamingPolicy.MultiDrmStreaming.ToString())
                {
                    type = AssetProtectionType.PlayReadyAndWidevineAndFairplay;
                }
            }

            return type;
        }


        public async Task<StringBuilder> GetStatsAsync()
        {
            StringBuilder sb = new StringBuilder();

            if (SelectedAssetsV3.Count > 0)
            {
                // Asset Stats
                foreach (Asset theAsset in SelectedAssetsV3)
                {
                    sb.Append(await GetStatAsync(theAsset, _amsClient));
                }
            }
            return sb;
        }
        /*
        public static StringBuilder GetStat(IAsset MyAsset, StreamingEndpoint SelectedSE = null)
        {
            StringBuilder sb = new StringBuilder();
            string MyAssetType = AssetInfo.GetAssetType(MyAsset);
            bool bfileinasset = (MyAsset.AssetFiles.Count() == 0) ? false : true;
            long size = -1;
            if (bfileinasset)
            {
                size = 0;
                foreach (IAssetFile file in MyAsset.AssetFiles)
                {
                    size += file.ContentFileSize;
                }
            }
            sb.AppendLine("Asset Name          : " + MyAsset.Name);
            sb.AppendLine("Asset Type          : " + MyAsset.AssetType);
            sb.AppendLine("Asset Id            : " + MyAsset.Id);
            sb.AppendLine("Alternate ID        : " + MyAsset.AlternateId);
            if (size != -1)
                sb.AppendLine("Size                : " + FormatByteSize(size));
            sb.AppendLine("State               : " + MyAsset.State);
            sb.AppendLine("Created (UTC)       : " + MyAsset.Created.ToLongDateString() + " " + MyAsset.Created.ToLongTimeString());
            sb.AppendLine("Last Modified (UTC) : " + MyAsset.LastModified.ToLongDateString() + " " + MyAsset.LastModified.ToLongTimeString());
            sb.AppendLine("Creations Options   : " + MyAsset.Options);

            if (MyAsset.State != AssetState.Deleted)
            {
                sb.AppendLine("IsStreamable        : " + MyAsset.IsStreamable);
                sb.AppendLine("SupportsDynEnc      : " + MyAsset.SupportsDynamicEncryption);
                sb.AppendLine("Uri                 : " + MyAsset.Uri.AbsoluteUri);
                sb.AppendLine("");
                sb.AppendLine("Storage Name        : " + MyAsset.StorageAccountName);
                sb.AppendLine("Storage Bytes used  : " + FormatByteSize(MyAsset.StorageAccount.BytesUsed));
                sb.AppendLine("Storage IsDefault   : " + MyAsset.StorageAccount.IsDefault);
                sb.AppendLine("");

                foreach (IAsset p_asset in MyAsset.ParentAssets)
                {
                    sb.AppendLine("Parent asset Name   : " + p_asset.Name);
                    sb.AppendLine("Parent asset Id     : " + p_asset.Id);
                }
                sb.AppendLine("");
                foreach (IContentKey key in MyAsset.ContentKeys)
                {
                    sb.AppendLine("Content key         : " + key.Name);
                    sb.AppendLine("Content key Id      : " + key.Id);
                    sb.AppendLine("Content key Type    : " + key.ContentKeyType);
                }
                sb.AppendLine("");
                foreach (var pol in MyAsset.DeliveryPolicies)
                {
                    sb.AppendLine("Deliv policy Name   : " + pol.Name);
                    sb.AppendLine("Deliv policy Id     : " + pol.Id);
                    sb.AppendLine("Deliv policy Type   : " + pol.AssetDeliveryPolicyType);
                    sb.AppendLine("Deliv pol Protocol  : " + pol.AssetDeliveryProtocol);
                }
                sb.AppendLine("");

                foreach (IAssetFile fileItem in MyAsset.AssetFiles)
                {
                    if (fileItem.IsPrimary)
                    {
                        sb.AppendLine("   ------------(-P-R-I-M-A-R-Y-)------------------");
                    }
                    else
                    {
                        sb.AppendLine("   -----------------------------------------------");
                    }
                    sb.AppendLine("   Name                 : " + fileItem.Name);
                    sb.AppendLine("   Id                   : " + fileItem.Id);
                    sb.AppendLine("   File size            : " + fileItem.ContentFileSize + " Bytes");
                    sb.AppendLine("   Mime type            : " + fileItem.MimeType);
                    sb.AppendLine("   Init vector          : " + fileItem.InitializationVector);
                    sb.AppendLine("   Created (UTC)        : " + fileItem.Created.ToString("G"));
                    sb.AppendLine("   Last modified (UTC)  : " + fileItem.LastModified.ToString("G"));
                    sb.AppendLine("   Encrypted            : " + fileItem.IsEncrypted);
                    sb.AppendLine("   EncryptionScheme     : " + fileItem.EncryptionScheme);
                    sb.AppendLine("   EncryptionVersion    : " + fileItem.EncryptionVersion);
                    sb.AppendLine("   Encryption key id    : " + fileItem.EncryptionKeyId);
                    sb.AppendLine("   InitializationVector : " + fileItem.InitializationVector);
                    sb.AppendLine("   ParentAssetId        : " + fileItem.ParentAssetId);
                    sb.AppendLine("");
                }
                sb.Append(GetDescriptionLocators(MyAsset, SelectedSE));
            }
            sb.AppendLine("");
            sb.AppendLine("+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
            sb.AppendLine("");

            return sb;
        }
        */
        public static async Task<StringBuilder> GetStatAsync(Asset MyAsset, AMSClientV3 _amsClient)
        {
            ListRepData infoStr = new ListRepData();

            AssetInfoData MyAssetTypeInfo = await AssetInfo.GetAssetTypeAsync(MyAsset.Name, _amsClient);
            if (MyAssetTypeInfo == null)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Error accessing asset type info");
                return sb;
            }

            bool bfileinasset = MyAssetTypeInfo.Blobs.Count() != 0;
            long size = -1;
            if (bfileinasset)
            {
                size = 0;
                foreach (IListBlobItem blob in MyAssetTypeInfo.Blobs.Where(b => b.GetType() == typeof(CloudBlockBlob)))
                {
                    size += (blob as CloudBlockBlob).Properties.Length;
                }
            }

            infoStr.Add("Asset Name", MyAsset.Name);
            infoStr.Add("Asset Description", MyAsset.Description);

            infoStr.Add("Asset Type", MyAssetTypeInfo.Type);
            infoStr.Add("Id", MyAsset.Id);
            infoStr.Add("Asset Id", MyAsset.AssetId.ToString());
            infoStr.Add("Alternate ID", MyAsset.AlternateId);
            if (size != -1)
            {
                infoStr.Add("Size", FormatByteSize(size));
            }

            infoStr.Add("Container", MyAsset.Container);
            infoStr.Add("Created (UTC)", MyAsset.Created.ToLongDateString() + " " + MyAsset.Created.ToLongTimeString());
            infoStr.Add("Last Modified (UTC)", MyAsset.LastModified.ToLongDateString() + " " + MyAsset.LastModified.ToLongTimeString());
            infoStr.Add("Storage account", MyAsset.StorageAccountName);
            infoStr.Add("Storage Encryption", MyAsset.StorageEncryptionFormat);

            infoStr.Add(string.Empty);

            foreach (IListBlobItem blob in MyAssetTypeInfo.Blobs)
            {
                infoStr.Add("   -----------------------------------------------");

                if (blob.GetType() == typeof(CloudBlockBlob))
                {
                    CloudBlockBlob blobc = blob as CloudBlockBlob;
                    infoStr.Add("   Block Blob Name", blobc.Name);
                    infoStr.Add("   Type", blobc.BlobType.ToString());
                    infoStr.Add("   Blob length", blobc.Properties.Length + " Bytes");
                    infoStr.Add("   Content type", blobc.Properties.ContentType);
                    infoStr.Add("   Created (UTC)", blobc.Properties.Created?.ToString("G"));
                    infoStr.Add("   Last modified (UTC)", blobc.Properties.LastModified?.ToString("G"));
                    infoStr.Add("   Server Encrypted", blobc.Properties.IsServerEncrypted.ToString());
                    infoStr.Add("   Content MD5", blobc.Properties.ContentMD5);
                    infoStr.Add(string.Empty);

                }
                else if (blob.GetType() == typeof(CloudBlobDirectory))
                {
                    CloudBlobDirectory blobd = blob as CloudBlobDirectory;
                    infoStr.Add("   Blob Directory Name", blobd.Prefix);
                    infoStr.Add("   Type", "BlobDirectory");
                    infoStr.Add("   Blob Director length", GetSizeBlobDirectory(blobd) + " Bytes");
                    infoStr.Add(string.Empty);
                }
            }
            infoStr.Add(await GetDescriptionLocatorsAsync(MyAsset, _amsClient));

            infoStr.Add(string.Empty);
            infoStr.Add("+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
            infoStr.Add(string.Empty);

            return infoStr.ReturnStringBuilder();
        }

        public static long GetSizeBlobDirectory(CloudBlobDirectory blobd)
        {
            long sizeDir = 0;
            List<CloudBlockBlob> subBlobs = blobd.ListBlobs(blobListingDetails: BlobListingDetails.Metadata).Where(b => b.GetType() == typeof(CloudBlockBlob)).Select(b => (CloudBlockBlob)b).ToList();
            subBlobs.ForEach(b => sizeDir += b.Properties.Length);

            return sizeDir;
        }


        public static async Task<ListRepData> GetDescriptionLocatorsAsync(Asset MyAsset, AMSClientV3 amsClient)
        {
            await amsClient.RefreshTokenIfNeededAsync();

            IList<AssetStreamingLocator> locators = (await amsClient.AMSclient.Assets.ListStreamingLocatorsAsync(amsClient.credentialsEntry.ResourceGroup, amsClient.credentialsEntry.AccountName, MyAsset.Name))
                                                    .StreamingLocators;

            ListRepData infoStr = new ListRepData();

            if (locators.Count == 0)
            {
                infoStr.Add("No streaming locator created for this asset.", null);
            }

            foreach (AssetStreamingLocator locatorbase in locators)
            {
                StreamingLocator locator = await amsClient.AMSclient.StreamingLocators.GetAsync(amsClient.credentialsEntry.ResourceGroup, amsClient.credentialsEntry.AccountName, locatorbase.Name);


                infoStr.Add("Locator Name", locator.Name);
                infoStr.Add("Locator Id", locator.StreamingLocatorId.ToString());
                infoStr.Add("Start Time", locator.StartTime?.ToLongDateString());
                infoStr.Add("End Time", locator.EndTime?.ToLongDateString());
                infoStr.Add("Streaming Policy Name", locator.StreamingPolicyName);
                infoStr.Add("Default Content Key Policy Name", locator.DefaultContentKeyPolicyName);
                infoStr.Add("Associated filters", string.Join(", ", locator.Filters.ToArray()));

                IList<StreamingPath> streamingPaths = (await amsClient.AMSclient.StreamingLocators.ListPathsAsync(amsClient.credentialsEntry.ResourceGroup, amsClient.credentialsEntry.AccountName, locator.Name)).StreamingPaths;
                IList<string> downloadPaths = (await amsClient.AMSclient.StreamingLocators.ListPathsAsync(amsClient.credentialsEntry.ResourceGroup, amsClient.credentialsEntry.AccountName, locator.Name)).DownloadPaths;

                foreach (StreamingPath path in streamingPaths)
                {
                    foreach (string p in path.Paths)
                    {
                        infoStr.Add(path.StreamingProtocol.ToString() + " " + path.EncryptionScheme, p);
                    }
                }

                foreach (string path in downloadPaths)
                {
                    infoStr.Add("Download", path);
                }

                infoStr.Add("==============================================================================");
                infoStr.Add(string.Empty);

            }
            return infoStr;
        }


        public static async Task<string> DoPlayBackWithStreamingEndpointAsync(PlayerType typeplayer, string path, AMSClientV3 client, Mainform mainForm,
            Asset myasset = null, bool DoNotRewriteURL = false, string filter = null, AssetProtectionType keytype = AssetProtectionType.None,
            AzureMediaPlayerFormats formatamp = AzureMediaPlayerFormats.Auto,
            AzureMediaPlayerTechnologies technology = AzureMediaPlayerTechnologies.Auto, bool launchbrowser = true, bool UISelectSEFiltersAndProtocols = true, string selectedBrowser = "",
            AssetStreamingLocator locator = null, string subtitleLanguageCode = null)
        {
            string FullPlayBackLink = null;

            if (!string.IsNullOrEmpty(path))
            {
                StreamingEndpoint choosenSE = await AssetInfo.GetBestStreamingEndpointAsync(client);

                if (choosenSE == null)
                {
                    return null;
                }

                // Let's ask for SE if several SEs or Custom Host Names or Filters
                if (!DoNotRewriteURL)
                {
                    ChooseStreamingEndpoint form = new ChooseStreamingEndpoint(client, myasset, path, filter, typeplayer, true);
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        path = AssetInfo.RW(form.UpdatedPath, form.SelectStreamingEndpoint, form.SelectedFilters, form.ReturnHttps, form.ReturnSelectCustomHostName, form.ReturnStreamingProtocol, form.ReturnHLSAudioTrackName, form.ReturnHLSNoAudioOnlyMode).ToString();
                        choosenSE = form.SelectStreamingEndpoint;
                        selectedBrowser = form.ReturnSelectedBrowser;
                    }
                    else
                    {
                        return string.Empty;
                    }
                }

                if (myasset != null)
                {
                    keytype = AssetInfo.GetAssetProtection(locator); // let's save the protection scheme (use by azure player): AES, PlayReady, Widevine or PlayReadyAndWidevine V3 migration
                }
            }

            // let's launch the player
            switch (typeplayer)
            {
                case PlayerType.AzureMediaPlayer:
                case PlayerType.AzureMediaPlayerFrame:
                case PlayerType.AzureMediaPlayerClear:

                    string playerurl = string.Empty;

                    if (keytype != AssetProtectionType.None)
                    {
                        bool insertoken = false;// !string.IsNullOrEmpty(tokenresult.TokenString);

                        if (insertoken)  // token. Let's analyse the token to find the drm technology used
                        {/*
                                switch (tokenresult.ContentKeyDeliveryType)
                                {
                                    case ContentKeyDeliveryType.BaselineHttp:
                                        playerurl += string.Format(Constants.AMPAes, true.ToString());
                                        playerurl += string.Format(Constants.AMPAesToken, tokenresult.TokenString);
                                        break;

                                    case ContentKeyDeliveryType.PlayReadyLicense:
                                        playerurl += string.Format(Constants.AMPPlayReady, true.ToString());
                                        playerurl += string.Format(Constants.AMPPlayReadyToken, tokenresult.TokenString);
                                        break;

                                    case ContentKeyDeliveryType.Widevine:
                                        playerurl += string.Format(Constants.AMPWidevine, true.ToString());
                                        playerurl += string.Format(Constants.AMPWidevineToken, tokenresult.TokenString);
                                        break;
                                        
                                    default:
                                        break;
                                }
                            */
                        }
                        else // No token. Open mode. Let's look to the key to know the drm technology
                        {
                            switch (keytype)
                            {
                                case AssetProtectionType.AES:
                                    playerurl += string.Format(Constants.AMPAes, true.ToString());
                                    break;

                                case AssetProtectionType.PlayReady:
                                    playerurl += string.Format(Constants.AMPPlayReady, true.ToString());
                                    break;

                                case AssetProtectionType.Widevine:
                                    playerurl += string.Format(Constants.AMPWidevine, true.ToString());
                                    break;

                                case AssetProtectionType.PlayReadyAndWidevine:
                                case AssetProtectionType.PlayReadyAndWidevineAndFairplay:
                                    playerurl += string.Format(Constants.AMPPlayReady, true.ToString());
                                    playerurl += string.Format(Constants.AMPWidevine, true.ToString());
                                    break;

                                default:
                                    break;
                            }
                        }
                    }

                    if (formatamp != AzureMediaPlayerFormats.Auto)
                    {
                        switch (formatamp)
                        {
                            case AzureMediaPlayerFormats.Dash:
                                playerurl += string.Format(Constants.AMPformatsyntax, "dash");
                                break;

                            case AzureMediaPlayerFormats.Smooth:
                                playerurl += string.Format(Constants.AMPformatsyntax, "smooth");
                                break;

                            case AzureMediaPlayerFormats.HLS:
                                playerurl += string.Format(Constants.AMPformatsyntax, "hls");
                                break;

                            case AzureMediaPlayerFormats.VideoMP4:
                                playerurl += string.Format(Constants.AMPformatsyntax, "video/mp4");
                                break;

                            default: // auto or other
                                break;
                        }
                        if (false)//tokenresult.TokenString != null)
                        {
                            //playerurl += string.Format(Constants.AMPtokensyntax, tokenresult);
                        }
                    }

                    if (technology != AzureMediaPlayerTechnologies.Auto)
                    {
                        switch (technology)
                        {
                            case AzureMediaPlayerTechnologies.Flash:
                                playerurl += string.Format(Constants.AMPtechsyntax, "flash");
                                break;

                            case AzureMediaPlayerTechnologies.JavaScript:
                                playerurl += string.Format(Constants.AMPtechsyntax, "js");
                                break;

                            case AzureMediaPlayerTechnologies.NativeHTML5:
                                playerurl += string.Format(Constants.AMPtechsyntax, "html5");
                                break;

                            case AzureMediaPlayerTechnologies.Silverlight:
                                playerurl += string.Format(Constants.AMPtechsyntax, "silverlight");
                                break;

                            default: // auto or other
                                break;
                        }
                    }

                    /* V3 migration
                    if (myasset != null) // wtt subtitles files
                    {
                        var subtitles = myasset.AssetFiles.ToList().Where(f => f.Name.ToLower().EndsWith(".vtt")).ToList();
                        if (subtitles.Count > 0)
                        {
                            var urlasset = new Uri(Urlstr);
                            string baseurlwith = urlasset.GetLeftPart(UriPartial.Authority) + urlasset.Segments[0] + urlasset.Segments[1];
                            var listsub = new List<string>();
                            foreach (var s in subtitles)
                            {
                                listsub.Add(Path.GetFileNameWithoutExtension(s.Name) + ",und," + HttpUtility.UrlEncode(baseurlwith + s.Name));
                            }
                            playerurl += string.Format(Constants.AMPSubtitles, string.Join(";", listsub));
                        }
                    }
                    */

                    string playerurlbase = string.Empty;
                    if (typeplayer == PlayerType.AzureMediaPlayer)
                    {
                        playerurlbase = Constants.PlayerAMPToLaunch;
                    }
                    else if (typeplayer == PlayerType.AzureMediaPlayerFrame)
                    {
                        playerurlbase = Constants.PlayerAMPIFrameToLaunch;
                    }
                    else if (typeplayer == PlayerType.AzureMediaPlayerClear)
                    {
                        playerurlbase = Constants.PlayerAMPToLaunch.Replace("https://", "http://");
                    }

                    if (subtitleLanguageCode != null) // let's add the subtitle syntax to AMP
                    {
                        try
                        {
                            CultureInfo culture = CultureInfo.GetCultureInfo(subtitleLanguageCode);
                            string trackName = WebUtility.HtmlEncode(culture.DisplayName);
                            playerurl += $"&imsc1Captions={trackName},{subtitleLanguageCode}";
                        }
                        catch
                        {

                        }
                    }

                    FullPlayBackLink = string.Format(playerurlbase, HttpUtility.UrlEncode(path)) + playerurl;
                    break;

                case PlayerType.DASHIFRefPlayer:
                    if (!path.Contains(string.Format(AssetInfo.format_url, AssetInfo.format_dash_csf)) && !path.Contains(string.Format(AssetInfo.format_url, AssetInfo.format_dash_cmaf)))
                    {
                        path = AssetInfo.AddParameterToUrlString(path, string.Format(AssetInfo.format_url, AssetInfo.format_dash_csf));
                    }
                    FullPlayBackLink = string.Format(Constants.PlayerDASHIFToLaunch, path);
                    break;

                case PlayerType.MP4AzurePage:
                    FullPlayBackLink = string.Format(Constants.PlayerMP4AzurePage, HttpUtility.UrlEncode(path));
                    break;


                case PlayerType.AdvancedTestPlayer:
                    string playerurlAd = string.Empty;
                    if (subtitleLanguageCode != null) // let's add the subtitle syntax to AMP
                    {
                        try
                        {
                            CultureInfo culture = CultureInfo.GetCultureInfo(subtitleLanguageCode);
                            string trackName = WebUtility.HtmlEncode(culture.DisplayName);
                            playerurlAd += $"&imsc1CaptionsSettings={trackName},{subtitleLanguageCode}";
                        }
                        catch
                        {

                        }
                    }
                    FullPlayBackLink = string.Format(Constants.AdvancedTestPlayer, HttpUtility.UrlEncode(path)) + playerurlAd;
                    break;

                case PlayerType.CustomPlayer:
                    string myurl = Properties.Settings.Default.CustomPlayerUrl;
                    FullPlayBackLink = myurl.Replace(Constants.NameconvManifestURL, HttpUtility.UrlEncode(path)).Replace(Constants.NameconvToken, string.Empty /*tokenresult.TokenString*/);
                    break;
            }

            if (FullPlayBackLink != null && launchbrowser)
            {
                try
                {
                    if (string.IsNullOrEmpty(selectedBrowser))
                    {
                        Process.Start(FullPlayBackLink);
                    }
                    else
                    {
                        if (selectedBrowser.Contains("edge"))
                        {
                            Process.Start(selectedBrowser + FullPlayBackLink);
                        }
                        else
                        {
                            Process.Start(selectedBrowser, FullPlayBackLink);
                        }
                    }
                }
                catch
                {
                    mainForm.TextBoxLogWriteLine("Error when launching the browser.", true);
                }
            }


            return FullPlayBackLink;
        }


        internal static async Task<StreamingEndpoint> GetBestStreamingEndpointAsync(AMSClientV3 client)
        {
            await client.RefreshTokenIfNeededAsync();
            IEnumerable<StreamingEndpoint> SEList = (await client.AMSclient.StreamingEndpoints.ListAsync(client.credentialsEntry.ResourceGroup, client.credentialsEntry.AccountName)).AsEnumerable();
            StreamingEndpoint SESelected = SEList.Where(se => se.ResourceState == StreamingEndpointResourceState.Running).OrderBy(se => se.CdnEnabled).OrderBy(se => se.ScaleUnits).LastOrDefault();
            if (SESelected == null)
            {
                SESelected = await client.AMSclient.StreamingEndpoints.GetAsync(client.credentialsEntry.ResourceGroup, client.credentialsEntry.AccountName, "default");
            }

            if (SESelected == null)
            {
                SESelected = SEList.FirstOrDefault();
            }

            return SESelected;
        }

        // copy a directory of the same container to another container
        public static List<Task> CopyBlobDirectory(CloudBlobDirectory srcDirectory, CloudBlobContainer destContainer, string sourceblobToken, CancellationToken token)
        {

            List<Task> mylistresults = new List<Task>();

            List<IListBlobItem> srcBlobList = srcDirectory.ListBlobs(
                useFlatBlobListing: true,
                blobListingDetails: BlobListingDetails.None).ToList();

            foreach (IListBlobItem src in srcBlobList)
            {
                ICloudBlob srcBlob = src as ICloudBlob;

                // Create appropriate destination blob type to match the source blob
                CloudBlob destBlob;
                if (srcBlob.Properties.BlobType == BlobType.BlockBlob)
                {
                    destBlob = destContainer.GetBlockBlobReference(srcBlob.Name);
                }
                else
                {
                    destBlob = destContainer.GetPageBlobReference(srcBlob.Name);
                }

                // copy using src blob as SAS
                mylistresults.Add(destBlob.StartCopyAsync(new Uri(srcBlob.Uri.AbsoluteUri + sourceblobToken), token));
            }

            return mylistresults;
        }


        public static string GetXMLSerializedTimeSpan(TimeSpan timespan)
        // return TimeSpan as a XML string: P28DT15H50M58.348S
        {
            DataContractSerializer serialize = new DataContractSerializer(typeof(TimeSpan));
            XNamespace ns = "http://schemas.microsoft.com/2003/10/Serialization/";

            using (MemoryStream ms = new MemoryStream())
            {
                serialize.WriteObject(ms, timespan);
                string xmlstart = Encoding.Default.GetString(ms.ToArray());
                // serialization is : <duration xmlns="http://schemas.microsoft.com/2003/10/Serialization/">P28DT15H50M58.348S</duration>
                return XDocument.Parse(xmlstart).Element(ns + "duration").Value.ToString();
            }
        }

        private static readonly List<string> InvalidFileNamePrefixList = new List<string>
                {
                    "CON",
                    "PRN",
                    "AUX",
                    "NUL",
                    "COM1",
                    "COM2",
                    "COM3",
                    "COM4",
                    "COM5",
                    "COM6",
                    "COM7",
                    "COM8",
                    "COM9",
                    "LPT1",
                    "LPT2",
                    "LPT3",
                    "LPT4",
                    "LPT5",
                    "LPT6",
                    "LPT7",
                    "LPT8",
                    "LPT9"
                };

        private static readonly char[] NtfsInvalidChars = System.IO.Path.GetInvalidFileNameChars();


        public static bool BlobNameForAMSIsOk(string filename)
        {
            // check if the blob name is compatible with AMS
            // Validates if the blob name conforms to the following requirements
            // blob name must be a valid blob name.
            // blob name must be a valid NTFS file name.
            // blob should not contain the following characters: [ ] { } + % and #
            // blob should not contain only space(s)
            // blob should not start with certain prefixes restricted by NTFS such as CON1, PRN ... 
            // A blob constructed using the above mentioned criteria shall be encoded, streamed and played back successfully.

            if (string.IsNullOrWhiteSpace(filename))
            {
                return false;
            }

            // let's make sure we exract the file name (without the path)
            filename = Path.GetFileName(filename);

            // white space
            if (string.IsNullOrWhiteSpace(filename))
            {
                return false;
            }

            if (filename.Length > 255)
            {
                return false;
            }

            Regex reg = new Regex(@"[+%#\[\]]", RegexOptions.Compiled);
            if (filename.IndexOfAny(NtfsInvalidChars) > 0 || reg.IsMatch(filename))
            {
                return false;
            }

            //// Invalid NTFS Filename prefix checks
            if (InvalidFileNamePrefixList.Any(x => filename.StartsWith(x + ".", StringComparison.OrdinalIgnoreCase)) ||
                InvalidFileNamePrefixList.Any(x => filename.Equals(x, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            // blob name requirements
            try
            {
                NameValidator.ValidateBlobName(filename);
            }
            catch
            {
                return false;
            }

            return true;
        }

        // Return the list of problematic filenames
        public static List<string> ReturnFilenamesWithProblem(List<string> filenames)
        {
            List<string> listreturn = new List<string>();
            foreach (string f in filenames)
            {
                if (!BlobNameForAMSIsOk(f))
                {
                    listreturn.Add(Path.GetFileName(f));
                }
            }
            return listreturn;
        }

        public static string FileNameProblemMessage(List<string> listpb)

        {
            if (listpb.Count == 1)
            {
                return "This file name is not compatible with Media Services :\n\n" + listpb.FirstOrDefault() + "\n\nFile name is restricted to blob name requirements and NTFS requirements" + "\n\nOperation aborted.";
            }
            else
            {
                return "These file names are not compatible with Media Services :\n\n" + string.Join("\n", listpb) + "\n\nFile name is restricted to blob name requirements and NTFS requirements" + "\n\nOperation aborted.";
            }
        }
    }

}
