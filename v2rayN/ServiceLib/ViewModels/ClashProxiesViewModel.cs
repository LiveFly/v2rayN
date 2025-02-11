using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System.Reactive;
using System.Reactive.Linq;
using static ServiceLib.Models.ClashProviders;
using static ServiceLib.Models.ClashProxies;

namespace ServiceLib.ViewModels
{
    public class ClashProxiesViewModel : MyReactiveObject
    {
        private Dictionary<String, ProxiesItem>? _proxies;
        private Dictionary<String, ProvidersItem>? _providers;
        private int _delayTimeout = 99999999;

        private IObservableCollection<ClashProxyModel> _proxyGroups = new ObservableCollectionExtended<ClashProxyModel>();
        private IObservableCollection<ClashProxyModel> _proxyDetails = new ObservableCollectionExtended<ClashProxyModel>();

        public IObservableCollection<ClashProxyModel> ProxyGroups => _proxyGroups;
        public IObservableCollection<ClashProxyModel> ProxyDetails => _proxyDetails;

        [Reactive]
        public ClashProxyModel SelectedGroup { get; set; }

        [Reactive]
        public ClashProxyModel SelectedDetail { get; set; }

        public ReactiveCommand<Unit, Unit> ProxiesReloadCmd { get; }
        public ReactiveCommand<Unit, Unit> ProxiesDelaytestCmd { get; }
        public ReactiveCommand<Unit, Unit> ProxiesDelaytestPartCmd { get; }
        public ReactiveCommand<Unit, Unit> ProxiesSelectActivityCmd { get; }

        [Reactive]
        public int RuleModeSelected { get; set; }

        [Reactive]
        public int SortingSelected { get; set; }

        [Reactive]
        public bool AutoRefresh { get; set; }

        public ClashProxiesViewModel(Func<EViewAction, object?, Task<bool>>? updateView)
        {
            _config = AppHandler.Instance.Config;
            _updateView = updateView;

            ProxiesReloadCmd = ReactiveCommand.CreateFromTask(async () =>
            {
                await ProxiesReload();
            });
            ProxiesDelaytestCmd = ReactiveCommand.CreateFromTask(async () =>
            {
                await ProxiesDelayTest(true);
            });

            ProxiesDelaytestPartCmd = ReactiveCommand.CreateFromTask(async () =>
            {
                await ProxiesDelayTest(false);
            });
            ProxiesSelectActivityCmd = ReactiveCommand.CreateFromTask(async () =>
            {
                await SetActiveProxy();
            });

            SelectedGroup = new();
            SelectedDetail = new();
            AutoRefresh = _config.clashUIItem.proxiesAutoRefresh;
            SortingSelected = _config.clashUIItem.proxiesSorting;
            RuleModeSelected = (int)_config.clashUIItem.ruleMode;

            this.WhenAnyValue(
               x => x.SelectedGroup,
               y => y != null && Utils.IsNotEmpty(y.name))
                   .Subscribe(c => RefreshProxyDetails(c));

            this.WhenAnyValue(
               x => x.RuleModeSelected,
               y => y >= 0)
                   .Subscribe(async c => await DoRulemodeSelected(c));

            this.WhenAnyValue(
               x => x.SortingSelected,
               y => y >= 0)
                  .Subscribe(c => DoSortingSelected(c));

            this.WhenAnyValue(
            x => x.AutoRefresh,
            y => y == true)
                .Subscribe(c => { _config.clashUIItem.proxiesAutoRefresh = AutoRefresh; });

            Init();
        }

        private async Task Init()
        {
            await ProxiesReload();
            await DelayTestTask();
        }

        private async Task DoRulemodeSelected(bool c)
        {
            if (!c)
            {
                return;
            }
            if (_config.clashUIItem.ruleMode == (ERuleMode)RuleModeSelected)
            {
                return;
            }
            await SetRuleModeCheck((ERuleMode)RuleModeSelected);
        }

        public async Task SetRuleModeCheck(ERuleMode mode)
        {
            if (_config.clashUIItem.ruleMode == mode)
            {
                return;
            }
            await SetRuleMode(mode);
        }

        private void DoSortingSelected(bool c)
        {
            if (!c)
            {
                return;
            }
            if (SortingSelected != _config.clashUIItem.proxiesSorting)
            {
                _config.clashUIItem.proxiesSorting = SortingSelected;
            }

            RefreshProxyDetails(c);
        }

        private void UpdateHandler(bool notify, string msg)
        {
            NoticeHandler.Instance.SendMessageEx(msg);
        }

        public async Task ProxiesReload()
        {
            await GetClashProxies(true);
            await ProxiesDelayTest();
        }

        public async Task ProxiesDelayTest()
        {
            await ProxiesDelayTest(true);
        }

        #region proxy function

        private async Task SetRuleMode(ERuleMode mode)
        {
            _config.clashUIItem.ruleMode = mode;

            if (mode != ERuleMode.Unchanged)
            {
                Dictionary<string, string> headers = new()
                {
                    { "mode", mode.ToString().ToLower() }
                };
                await ClashApiHandler.Instance.ClashConfigUpdate(headers);
            }
        }

        private async Task GetClashProxies(bool refreshUI)
        {
            var ret = await ClashApiHandler.Instance.GetClashProxiesAsync(_config);
            if (ret?.Item1 == null || ret.Item2 == null)
            {
                return;
            }
            _proxies = ret.Item1.proxies;
            _providers = ret?.Item2.providers;

            if (refreshUI)
            {
                _updateView?.Invoke(EViewAction.DispatcherRefreshProxyGroups, null);
            }
        }

        public void RefreshProxyGroups()
        {
            var selectedName = SelectedGroup?.name;
            _proxyGroups.Clear();

            var proxyGroups = ClashApiHandler.Instance.GetClashProxyGroups();
            if (proxyGroups != null && proxyGroups.Count > 0)
            {
                foreach (var it in proxyGroups)
                {
                    if (Utils.IsNullOrEmpty(it.name) || !_proxies.ContainsKey(it.name))
                    {
                        continue;
                    }
                    var item = _proxies[it.name];
                    if (!Global.allowSelectType.Contains(item.type.ToLower()))
                    {
                        continue;
                    }
                    _proxyGroups.Add(new ClashProxyModel()
                    {
                        now = item.now,
                        name = item.name,
                        type = item.type
                    });
                }
            }

            //from api
            foreach (KeyValuePair<string, ProxiesItem> kv in _proxies)
            {
                if (!Global.allowSelectType.Contains(kv.Value.type.ToLower()))
                {
                    continue;
                }
                var item = _proxyGroups.Where(t => t.name == kv.Key).FirstOrDefault();
                if (item != null && Utils.IsNotEmpty(item.name))
                {
                    continue;
                }
                _proxyGroups.Add(new ClashProxyModel()
                {
                    now = kv.Value.now,
                    name = kv.Key,
                    type = kv.Value.type
                });
            }

            if (_proxyGroups != null && _proxyGroups.Count > 0)
            {
                if (selectedName != null && _proxyGroups.Any(t => t.name == selectedName))
                {
                    SelectedGroup = _proxyGroups.FirstOrDefault(t => t.name == selectedName);
                }
                else
                {
                    SelectedGroup = _proxyGroups[0];
                }
            }
            else
            {
                SelectedGroup = new();
            }
        }

        private void RefreshProxyDetails(bool c)
        {
            _proxyDetails.Clear();
            if (!c)
            {
                return;
            }
            var name = SelectedGroup?.name;
            if (Utils.IsNullOrEmpty(name))
            {
                return;
            }
            if (_proxies == null)
            {
                return;
            }

            _proxies.TryGetValue(name, out ProxiesItem proxy);
            if (proxy == null || proxy.all == null)
            {
                return;
            }
            var lstDetails = new List<ClashProxyModel>();
            foreach (var item in proxy.all)
            {
                var isActive = item == proxy.now;

                var proxy2 = TryGetProxy(item);
                if (proxy2 == null)
                {
                    continue;
                }
                int delay = -1;
                if (proxy2.history.Count > 0)
                {
                    delay = proxy2.history[proxy2.history.Count - 1].delay;
                }

                lstDetails.Add(new ClashProxyModel()
                {
                    isActive = isActive,
                    name = item,
                    type = proxy2.type,
                    delay = delay <= 0 ? _delayTimeout : delay,
                    delayName = delay <= 0 ? string.Empty : $"{delay}ms",
                });
            }
            //sort
            switch (SortingSelected)
            {
                case 0:
                    lstDetails = lstDetails.OrderBy(t => t.delay).ToList();
                    break;

                case 1:
                    lstDetails = lstDetails.OrderBy(t => t.name).ToList();
                    break;

                default:
                    break;
            }
            _proxyDetails.AddRange(lstDetails);
        }

        private ProxiesItem? TryGetProxy(string name)
        {
            if (_proxies is null)
                return null;
            _proxies.TryGetValue(name, out ProxiesItem proxy2);
            if (proxy2 != null)
            {
                return proxy2;
            }
            //from providers
            if (_providers != null)
            {
                foreach (KeyValuePair<string, ProvidersItem> kv in _providers)
                {
                    if (Global.proxyVehicleType.Contains(kv.Value.vehicleType.ToLower()))
                    {
                        var proxy3 = kv.Value.proxies.FirstOrDefault(t => t.name == name);
                        if (proxy3 != null)
                        {
                            return proxy3;
                        }
                    }
                }
            }
            return null;
        }

        public async Task SetActiveProxy()
        {
            if (SelectedGroup == null || Utils.IsNullOrEmpty(SelectedGroup.name))
            {
                return;
            }
            if (SelectedDetail == null || Utils.IsNullOrEmpty(SelectedDetail.name))
            {
                return;
            }
            var name = SelectedGroup.name;
            if (Utils.IsNullOrEmpty(name))
            {
                return;
            }
            var nameNode = SelectedDetail.name;
            if (Utils.IsNullOrEmpty(nameNode))
            {
                return;
            }
            var selectedProxy = TryGetProxy(name);
            if (selectedProxy == null || selectedProxy.type != "Selector")
            {
                NoticeHandler.Instance.Enqueue(ResUI.OperationFailed);
                return;
            }

            await ClashApiHandler.Instance.ClashSetActiveProxy(name, nameNode);

            selectedProxy.now = nameNode;
            var group = _proxyGroups.Where(it => it.name == SelectedGroup.name).FirstOrDefault();
            if (group != null)
            {
                group.now = nameNode;
                var group2 = JsonUtils.DeepCopy(group);
                _proxyGroups.Replace(group, group2);

                SelectedGroup = group2;
            }
            NoticeHandler.Instance.Enqueue(ResUI.OperationSuccess);
        }

        private async Task ProxiesDelayTest(bool blAll)
        {
            //UpdateHandler(false, "Clash Proxies Latency Test");

            ClashApiHandler.Instance.ClashProxiesDelayTest(blAll, _proxyDetails.ToList(), async (item, result) =>
            {
                if (item == null)
                {
                    await GetClashProxies(true);
                    return;
                }
                if (Utils.IsNullOrEmpty(result))
                {
                    return;
                }

                _updateView?.Invoke(EViewAction.DispatcherProxiesDelayTest, new SpeedTestResult() { IndexId = item.name, Delay = result });
            });
        }

        public void ProxiesDelayTestResult(SpeedTestResult result)
        {
            //UpdateHandler(false, $"{item.name}={result}");
            var detail = _proxyDetails.Where(it => it.name == result.IndexId).FirstOrDefault();
            if (detail != null)
            {
                var dicResult = JsonUtils.Deserialize<Dictionary<string, object>>(result.Delay);
                if (dicResult != null && dicResult.ContainsKey("delay"))
                {
                    detail.delay = Convert.ToInt32(dicResult["delay"].ToString());
                    detail.delayName = $"{detail.delay}ms";
                }
                else if (dicResult != null && dicResult.ContainsKey("message"))
                {
                    detail.delay = _delayTimeout;
                    detail.delayName = $"{dicResult["message"]}";
                }
                else
                {
                    detail.delay = _delayTimeout;
                    detail.delayName = string.Empty;
                }
                _proxyDetails.Replace(detail, JsonUtils.DeepCopy(detail));
            }
        }

        #endregion proxy function

        #region task

        public async Task DelayTestTask()
        {
            var lastTime = DateTime.Now;

            Observable.Interval(TimeSpan.FromSeconds(60))
              .Subscribe(async x =>
              {
                  if (!(AutoRefresh && _config.uiItem.showInTaskbar && _config.IsRunningCore(ECoreType.sing_box)))
                  {
                      return;
                  }
                  var dtNow = DateTime.Now;
                  if (_config.clashUIItem.proxiesAutoDelayTestInterval > 0)
                  {
                      if ((dtNow - lastTime).Minutes % _config.clashUIItem.proxiesAutoDelayTestInterval == 0)
                      {
                          await ProxiesDelayTest();
                          lastTime = dtNow;
                      }
                      Task.Delay(1000).Wait();
                  }
              });
        }

        #endregion task
    }
}