﻿using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ServiceLib.Services
{
    public class UpdateService
    {
        private Action<bool, string>? _updateFunc;
        private Config _config;
        private int _timeout = 30;

        public async Task CheckUpdateGuiN(Config config, Action<bool, string> updateFunc, bool preRelease)
        {
            _config = config;
            _updateFunc = updateFunc;
            var url = string.Empty;
            var fileName = string.Empty;

            DownloadService downloadHandle = new();
            downloadHandle.UpdateCompleted += (sender2, args) =>
            {
                if (args.Success)
                {
                    _updateFunc?.Invoke(false, ResUI.MsgDownloadV2rayCoreSuccessfully);
                    _updateFunc?.Invoke(true, Utils.UrlEncode(fileName));
                }
                else
                {
                    _updateFunc?.Invoke(false, args.Msg);
                }
            };
            downloadHandle.Error += (sender2, args) =>
            {
                _updateFunc?.Invoke(false, args.GetException().Message);
            };

            _updateFunc?.Invoke(false, string.Format(ResUI.MsgStartUpdating, ECoreType.v2rayN));
            var result = await CheckUpdateAsync(downloadHandle, ECoreType.v2rayN, preRelease);
            if (result.Success)
            {
                _updateFunc?.Invoke(false, string.Format(ResUI.MsgParsingSuccessfully, ECoreType.v2rayN));
                _updateFunc?.Invoke(false, result.Msg);

                url = result.Data?.ToString();
                fileName = Utils.GetTempPath(Utils.GetGuid());
                await downloadHandle.DownloadFileAsync(url, fileName, true, _timeout);
            }
            else
            {
                _updateFunc?.Invoke(false, result.Msg);
            }
        }

        public async Task CheckUpdateCore(ECoreType type, Config config, Action<bool, string> updateFunc, bool preRelease)
        {
            _config = config;
            _updateFunc = updateFunc;
            var url = string.Empty;
            var fileName = string.Empty;

            DownloadService downloadHandle = new();
            downloadHandle.UpdateCompleted += (sender2, args) =>
            {
                if (args.Success)
                {
                    _updateFunc?.Invoke(false, ResUI.MsgDownloadV2rayCoreSuccessfully);
                    _updateFunc?.Invoke(false, ResUI.MsgUnpacking);

                    try
                    {
                        _updateFunc?.Invoke(true, fileName);
                    }
                    catch (Exception ex)
                    {
                        _updateFunc?.Invoke(false, ex.Message);
                    }
                }
                else
                {
                    _updateFunc?.Invoke(false, args.Msg);
                }
            };
            downloadHandle.Error += (sender2, args) =>
            {
                _updateFunc?.Invoke(false, args.GetException().Message);
            };

            _updateFunc?.Invoke(false, string.Format(ResUI.MsgStartUpdating, type));
            var result = await CheckUpdateAsync(downloadHandle, type, preRelease);
            if (result.Success)
            {
                _updateFunc?.Invoke(false, string.Format(ResUI.MsgParsingSuccessfully, type));
                _updateFunc?.Invoke(false, result.Msg);

                url = result.Data?.ToString();
                var ext = url.Contains(".tar.gz") ? ".tar.gz" : Path.GetExtension(url);
                fileName = Utils.GetTempPath(Utils.GetGuid() + ext);
                await downloadHandle.DownloadFileAsync(url, fileName, true, _timeout);
            }
            else
            {
                if (!result.Msg.IsNullOrEmpty())
                {
                    _updateFunc?.Invoke(false, result.Msg);
                }
            }
        }

        public async Task UpdateSubscriptionProcess(Config config, string subId, bool blProxy, Action<bool, string> updateFunc)
        {
            _config = config;
            _updateFunc = updateFunc;

            _updateFunc?.Invoke(false, ResUI.MsgUpdateSubscriptionStart);
            var subItem = await AppHandler.Instance.SubItems();

            if (subItem == null || subItem.Count <= 0)
            {
                _updateFunc?.Invoke(false, ResUI.MsgNoValidSubscription);
                return;
            }

            foreach (var item in subItem)
            {
                string id = item.id.TrimEx();
                string url = item.url.TrimEx();
                string userAgent = item.userAgent.TrimEx();
                string hashCode = $"{item.remarks}->";
                if (Utils.IsNullOrEmpty(id) || Utils.IsNullOrEmpty(url) || Utils.IsNotEmpty(subId) && item.id != subId)
                {
                    //_updateFunc?.Invoke(false, $"{hashCode}{ResUI.MsgNoValidSubscription}");
                    continue;
                }
                if (!url.StartsWith(Global.HttpsProtocol) && !url.StartsWith(Global.HttpProtocol))
                {
                    continue;
                }
                if (item.enabled == false)
                {
                    _updateFunc?.Invoke(false, $"{hashCode}{ResUI.MsgSkipSubscriptionUpdate}");
                    continue;
                }

                var downloadHandle = new DownloadService();
                downloadHandle.Error += (sender2, args) =>
                {
                    _updateFunc?.Invoke(false, $"{hashCode}{args.GetException().Message}");
                };

                _updateFunc?.Invoke(false, $"{hashCode}{ResUI.MsgStartGettingSubscriptions}");

                //one url
                url = Utils.GetPunycode(url);
                //convert
                if (Utils.IsNotEmpty(item.convertTarget))
                {
                    var subConvertUrl = Utils.IsNullOrEmpty(config.constItem.subConvertUrl) ? Global.SubConvertUrls.FirstOrDefault() : config.constItem.subConvertUrl;
                    url = string.Format(subConvertUrl!, Utils.UrlEncode(url));
                    if (!url.Contains("target="))
                    {
                        url += string.Format("&target={0}", item.convertTarget);
                    }
                    if (!url.Contains("config="))
                    {
                        url += string.Format("&config={0}", Global.SubConvertConfig.FirstOrDefault());
                    }
                }
                var result = await downloadHandle.TryDownloadString(url, blProxy, userAgent);
                if (blProxy && Utils.IsNullOrEmpty(result))
                {
                    result = await downloadHandle.TryDownloadString(url, false, userAgent);
                }

                //more url
                if (Utils.IsNullOrEmpty(item.convertTarget) && Utils.IsNotEmpty(item.moreUrl.TrimEx()))
                {
                    if (Utils.IsNotEmpty(result) && Utils.IsBase64String(result))
                    {
                        result = Utils.Base64Decode(result);
                    }

                    var lstUrl = item.moreUrl.TrimEx().Split(",") ?? [];
                    foreach (var it in lstUrl)
                    {
                        var url2 = Utils.GetPunycode(it);
                        if (Utils.IsNullOrEmpty(url2))
                        {
                            continue;
                        }

                        var result2 = await downloadHandle.TryDownloadString(url2, blProxy, userAgent);
                        if (blProxy && Utils.IsNullOrEmpty(result2))
                        {
                            result2 = await downloadHandle.TryDownloadString(url2, false, userAgent);
                        }
                        if (Utils.IsNotEmpty(result2))
                        {
                            if (Utils.IsBase64String(result2))
                            {
                                result += Utils.Base64Decode(result2);
                            }
                            else
                            {
                                result += result2;
                            }
                        }
                    }
                }

                if (Utils.IsNullOrEmpty(result))
                {
                    _updateFunc?.Invoke(false, $"{hashCode}{ResUI.MsgSubscriptionDecodingFailed}");
                }
                else
                {
                    _updateFunc?.Invoke(false, $"{hashCode}{ResUI.MsgGetSubscriptionSuccessfully}");
                    if (result?.Length < 99)
                    {
                        _updateFunc?.Invoke(false, $"{hashCode}{result}");
                    }

                    int ret = await ConfigHandler.AddBatchServers(config, result, id, true);
                    if (ret <= 0)
                    {
                        Logging.SaveLog("FailedImportSubscription");
                        Logging.SaveLog(result);
                    }
                    _updateFunc?.Invoke(false,
                        ret > 0
                            ? $"{hashCode}{ResUI.MsgUpdateSubscriptionEnd}"
                            : $"{hashCode}{ResUI.MsgFailedImportSubscription}");
                }
                _updateFunc?.Invoke(false, "-------------------------------------------------------");
            }

            _updateFunc?.Invoke(true, $"{ResUI.MsgUpdateSubscriptionEnd}");
        }

        public async Task UpdateGeoFileAll(Config config, Action<bool, string> updateFunc)
        {
            await UpdateGeoFile("geosite", config, updateFunc);
            await UpdateGeoFile("geoip", config, updateFunc);
            await UpdateSrsFileAll(config, updateFunc);
            _updateFunc?.Invoke(true, string.Format(ResUI.MsgDownloadGeoFileSuccessfully, "geo"));
        }

        public async Task RunAvailabilityCheck(Action<bool, string> updateFunc)
        {
            var time = await new DownloadService().RunAvailabilityCheck(null);
            updateFunc?.Invoke(false, string.Format(ResUI.TestMeOutput, time));
        }

        #region CheckUpdate private

        private async Task<RetResult> CheckUpdateAsync(DownloadService downloadHandle, ECoreType type, bool preRelease)
        {
            try
            {
                var result = await GetRemoteVersion(downloadHandle, type, preRelease);
                if (!result.Success || result.Data is null)
                {
                    return result;
                }
                return await ParseDownloadUrl(type, (SemanticVersion)result.Data);
            }
            catch (Exception ex)
            {
                Logging.SaveLog(ex.Message, ex);
                _updateFunc?.Invoke(false, ex.Message);
                return new RetResult(false, ex.Message);
            }
        }

        private async Task<RetResult> GetRemoteVersion(DownloadService downloadHandle, ECoreType type, bool preRelease)
        {
            var coreInfo = CoreInfoHandler.Instance.GetCoreInfo(type);
            var tagName = string.Empty;
            if (preRelease)
            {
                var url = coreInfo?.ReleaseApiUrl;
                var result = await downloadHandle.TryDownloadString(url, true, Global.AppName);
                if (Utils.IsNullOrEmpty(result))
                {
                    return new RetResult(false, "");
                }

                var gitHubReleases = JsonUtils.Deserialize<List<GitHubRelease>>(result);
                var gitHubRelease = preRelease ? gitHubReleases?.First() : gitHubReleases?.First(r => r.Prerelease == false);
                tagName = gitHubRelease?.TagName;
                //var body = gitHubRelease?.Body;
            }
            else
            {
                var url = Path.Combine(coreInfo.Url, "latest");
                var lastUrl = await downloadHandle.UrlRedirectAsync(url, true);
                if (lastUrl == null)
                {
                    return new RetResult(false, "");
                }

                tagName = lastUrl?.Split("/tag/").LastOrDefault();
            }
            return new RetResult(true, "", new SemanticVersion(tagName));
        }

        private async Task<SemanticVersion> GetCoreVersion(ECoreType type)
        {
            try
            {
                var coreInfo = CoreInfoHandler.Instance.GetCoreInfo(type);
                string filePath = string.Empty;
                foreach (string name in coreInfo.CoreExes)
                {
                    string vName = Utils.GetExeName(name);
                    vName = Utils.GetBinPath(vName, coreInfo.CoreType.ToString());
                    if (File.Exists(vName))
                    {
                        filePath = vName;
                        break;
                    }
                }

                if (!File.Exists(filePath))
                {
                    string msg = string.Format(ResUI.NotFoundCore, @"", "", "");
                    //ShowMsg(true, msg);
                    return new SemanticVersion("");
                }

                var result = await Utils.GetCliWrapOutput(filePath, coreInfo.VersionArg);
                var echo = result ?? "";
                string version = string.Empty;
                switch (type)
                {
                    case ECoreType.v2fly:
                    case ECoreType.Xray:
                    case ECoreType.v2fly_v5:
                        version = Regex.Match(echo, $"{coreInfo.Match} ([0-9.]+) \\(").Groups[1].Value;
                        break;

                    case ECoreType.mihomo:
                        version = Regex.Match(echo, $"v[0-9.]+").Groups[0].Value;
                        break;

                    case ECoreType.sing_box:
                        version = Regex.Match(echo, $"([0-9.]+)").Groups[1].Value;
                        break;
                }
                return new SemanticVersion(version);
            }
            catch (Exception ex)
            {
                Logging.SaveLog(ex.Message, ex);
                _updateFunc?.Invoke(false, ex.Message);
                return new SemanticVersion("");
            }
        }

        private async Task<RetResult> ParseDownloadUrl(ECoreType type, SemanticVersion version)
        {
            try
            {
                var coreInfo = CoreInfoHandler.Instance.GetCoreInfo(type);
                SemanticVersion curVersion;
                string message;
                string? url;
                switch (type)
                {
                    case ECoreType.v2fly:
                    case ECoreType.Xray:
                    case ECoreType.v2fly_v5:
                        {
                            curVersion = await GetCoreVersion(type);
                            message = string.Format(ResUI.IsLatestCore, type, curVersion.ToVersionString("v"));
                            url = string.Format(GetUrlFromCore(coreInfo), version.ToVersionString("v"));
                            break;
                        }
                    case ECoreType.mihomo:
                        {
                            curVersion = await GetCoreVersion(type);
                            message = string.Format(ResUI.IsLatestCore, type, curVersion);
                            url = string.Format(GetUrlFromCore(coreInfo), version.ToVersionString("v"));
                            break;
                        }
                    case ECoreType.sing_box:
                        {
                            curVersion = await GetCoreVersion(type);
                            message = string.Format(ResUI.IsLatestCore, type, curVersion.ToVersionString("v"));
                            url = string.Format(GetUrlFromCore(coreInfo), version.ToVersionString("v"), version);
                            break;
                        }
                    case ECoreType.v2rayN:
                        {
                            curVersion = new SemanticVersion(Utils.GetVersionInfo());
                            message = string.Format(ResUI.IsLatestN, type, curVersion);
                            url = string.Format(GetUrlFromCore(coreInfo), version);
                            break;
                        }
                    default:
                        throw new ArgumentException("Type");
                }

                if (curVersion >= version && version != new SemanticVersion(0, 0, 0))
                {
                    return new RetResult(false, message);
                }

                return new RetResult(true, "", url);
            }
            catch (Exception ex)
            {
                Logging.SaveLog(ex.Message, ex);
                _updateFunc?.Invoke(false, ex.Message);
                return new RetResult(false, ex.Message);
            }
        }

        private string? GetUrlFromCore(CoreInfo? coreInfo)
        {
            if (Utils.IsWindows())
            {
                //Check for standalone windows .Net version
                if (coreInfo?.CoreType == ECoreType.v2rayN
                    && File.Exists(Path.Combine(Utils.StartupPath(), "wpfgfx_cor3.dll"))
                    && File.Exists(Path.Combine(Utils.StartupPath(), "D3DCompiler_47_cor3.dll"))
                   )
                {
                    return coreInfo?.DownloadUrlWin64?.Replace(".zip", "-SelfContained.zip");
                }

                return RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.Arm64 => coreInfo?.DownloadUrlWinArm64,
                    Architecture.X86 => coreInfo?.DownloadUrlWin32,
                    Architecture.X64 => coreInfo?.DownloadUrlWin64,
                    _ => null,
                };
            }
            else if (Utils.IsLinux())
            {
                return RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.Arm64 => coreInfo?.DownloadUrlLinuxArm64,
                    Architecture.X64 => coreInfo?.DownloadUrlLinux64,
                    _ => null,
                };
            }
            return null;
        }

        #endregion CheckUpdate private

        #region Geo private

        private async Task UpdateGeoFile(string geoName, Config config, Action<bool, string> updateFunc)
        {
            _config = config;
            _updateFunc = updateFunc;

            var geoUrl = string.IsNullOrEmpty(config?.constItem.geoSourceUrl)
                ? Global.GeoUrl
                : config.constItem.geoSourceUrl;

            var fileName = $"{geoName}.dat";
            var targetPath = Utils.GetBinPath($"{fileName}");
            var url = string.Format(geoUrl, geoName);

            await DownloadGeoFile(url, fileName, targetPath, updateFunc);
        }

        private async Task UpdateSrsFileAll(Config config, Action<bool, string> updateFunc)
        {
            _config = config;
            _updateFunc = updateFunc;

            var geoipFiles = new List<string>();
            var geoSiteFiles = new List<string>();

            //Collect used files list
            var routingItems = await AppHandler.Instance.RoutingItems();
            foreach (var routing in routingItems)
            {
                var rules = JsonUtils.Deserialize<List<RulesItem>>(routing.ruleSet);
                foreach (var item in rules ?? [])
                {
                    foreach (var ip in item.ip ?? [])
                    {
                        var prefix = "geoip:";
                        if (ip.StartsWith(prefix))
                        {
                            geoipFiles.Add(ip.Substring(prefix.Length));
                        }
                    }

                    foreach (var domain in item.domain ?? [])
                    {
                        var prefix = "geosite:";
                        if (domain.StartsWith(prefix))
                        {
                            geoSiteFiles.Add(domain.Substring(prefix.Length));
                        }
                    }
                }
            }

            foreach (var item in geoipFiles.Distinct())
            {
                await UpdateSrsFile("geoip", item, config, updateFunc);
            }

            foreach (var item in geoSiteFiles.Distinct())
            {
                await UpdateSrsFile("geosite", item, config, updateFunc);
            }
        }

        private async Task UpdateSrsFile(string type, string srsName, Config config, Action<bool, string> updateFunc)
        {
            var srsUrl = string.IsNullOrEmpty(_config.constItem.srsSourceUrl)
                            ? Global.SingboxRulesetUrl
                            : _config.constItem.srsSourceUrl;

            var fileName = $"{type}-{srsName}.srs";
            var targetPath = Path.Combine(Utils.GetBinPath("srss"), fileName);
            var url = string.Format(srsUrl, type, $"{type}-{srsName}");

            await DownloadGeoFile(url, fileName, targetPath, updateFunc);
        }

        private async Task DownloadGeoFile(string url, string fileName, string targetPath, Action<bool, string> updateFunc)
        {
            var tmpFileName = Utils.GetTempPath(Utils.GetGuid());

            DownloadService downloadHandle = new();
            downloadHandle.UpdateCompleted += (sender2, args) =>
            {
                if (args.Success)
                {
                    _updateFunc?.Invoke(false, string.Format(ResUI.MsgDownloadGeoFileSuccessfully, fileName));

                    try
                    {
                        if (File.Exists(tmpFileName))
                        {
                            File.Copy(tmpFileName, targetPath, true);

                            File.Delete(tmpFileName);
                            //_updateFunc?.Invoke(true, "");
                        }
                    }
                    catch (Exception ex)
                    {
                        _updateFunc?.Invoke(false, ex.Message);
                    }
                }
                else
                {
                    _updateFunc?.Invoke(false, args.Msg);
                }
            };
            downloadHandle.Error += (sender2, args) =>
            {
                _updateFunc?.Invoke(false, args.GetException().Message);
            };

            await downloadHandle.DownloadFileAsync(url, tmpFileName, true, _timeout);
        }

        #endregion Geo private
    }
}