﻿using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia.Controls;
using Avalonia.Threading;
using ReactiveUI;
using Serilog;

using Atomex.Client.Common;
using Atomex.Client.Desktop.Controls;
using Atomex.Client.Desktop.Properties;
using Atomex.Common;
using Atomex.Wallet;

namespace Atomex.Client.Desktop.ViewModels
{
    public sealed class MainWindowViewModel : ViewModelBase
    {
        private void ShowContent(ViewModelBase content)
        {
            if (Content?.GetType() != content.GetType())
            {
                Content = content;
            }
        }

        private void ShowStart()
        {
            ShowContent(new StartViewModel(ShowContent, ShowStart, _app, this));
        }

        private ViewModelBase _content;

        public ViewModelBase Content
        {
            get => _content;
            set => this.RaiseAndSetIfChanged(ref _content, value);
        }

        private WalletMainViewModel WalletMainViewModel;

        public MainWindowViewModel()
        {
#if DEBUG
            if (Design.IsDesignMode)
                DesignerMode();
#endif
        }

        public Action OnUpdateAction;

        public MainWindowViewModel(IAtomexApp app, IMainView mainView = null)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            WalletMainViewModel = new WalletMainViewModel(_app);

            SubscribeToServices();

            if (mainView != null)
            {
                MainView = mainView;
                MainView.Inactivity += InactivityHandler;
            }

            ShowStart();
        }

        private IAtomexApp _app;
        private IMainView MainView { get; set; }


        private bool _hasAccount;

        public bool HasAccount
        {
            get => _hasAccount;
            set
            {
                _hasAccount = value;
                if (_hasAccount)
                {
                    ShowContent(WalletMainViewModel);

                    if (AccountRestored)
                    {
                        var restoreViewModel = new RestoreDialogViewModel(_app)
                        {
                            OnRestored = () => AccountRestored = false
                        };

                        restoreViewModel.ScanCurrenciesAsync();
                    }
                }

                this.RaisePropertyChanged(nameof(HasAccount));
            }
        }

        public bool AccountRestored { get; set; }
        public bool UpdatesReady => HasUpdates && UpdateDownloadProgress == 100;
        public bool IsDownloadingUpdate => HasUpdates && UpdateDownloadProgress > 0 && UpdateDownloadProgress < 100;

        private bool _hasUpdates;
        public bool HasUpdates
        {
            get => _hasUpdates;
            set
            {
                _hasUpdates = value;
                OnPropertyChanged(nameof(HasUpdates));
            }
        }

        private string _updateVersion;
        public string UpdateVersion
        {
            get => _updateVersion;
            set
            {
                _updateVersion = value;
                OnPropertyChanged(nameof(UpdateVersion));
            }
        }

        private bool _updateStarted;
        public bool UpdateStarted
        {
            get => _updateStarted;
            set
            {
                _updateStarted = value;
                OnPropertyChanged(nameof(UpdateStarted));
            }
        }

        private int _updateDownloadProgress;
        public int UpdateDownloadProgress
        {
            get => _updateDownloadProgress;
            set
            {
                if (_updateDownloadProgress != value)
                {
                    _updateDownloadProgress = value;
                    OnPropertyChanged(nameof(UpdateDownloadProgress));
                    OnPropertyChanged(nameof(IsDownloadingUpdate));
                    OnPropertyChanged(nameof(UpdatesReady));
                }
            }
        }

        private void SubscribeToServices()
        {
            _app.AtomexClientChanged += OnAtomexClientChangedEventHandler;
        }

        private void OnAtomexClientChangedEventHandler(object sender, AtomexClientChangedEventArgs args)
        {
            if (_app?.Account == null)
            {
                HasAccount = false;
                MainView?.StopInactivityControl();

                return;
            }

            HasAccount = true;

            // auto sign out after timeout
            if (MainView != null && _app.Account.UserData.AutoSignOut)
                MainView.StartInactivityControl(TimeSpan.FromMinutes(_app.Account.UserData.PeriodOfInactivityInMin));

            StartLookingForUserMessages(TimeSpan.FromSeconds(90));
        }


        private ICommand _updateCommand;
        public ICommand UpdateCommand => _updateCommand ??= ReactiveCommand.Create(OnUpdateClick);

        private async void OnUpdateClick()
        {
            await SignOut(withAppUpdate: true);
            if (_app.AtomexClient != null) return;

            OnUpdateAction?.Invoke();
            UpdateStarted = true;
        }

        private ICommand _signOutCommand;
        public ICommand SignOutCommand => _signOutCommand ??= ReactiveCommand.Create(() => SignOut());

        private bool _userIgnoreActiveSwaps { get; set; }

        private UnlockViewModel _unlockViewModel { get; set; }

        private async Task SignOut(bool withAppUpdate = false)
        {
            try
            {
                if (await WhetherToCancelClosingAsync() && !_userIgnoreActiveSwaps)
                {
                    var messageViewModel = MessageViewModel.Message(
                        title: "Warning",
                        text: Resources.ActiveSwapsWarning,
                        nextTitle: "Close",
                        backAction: () => App.DialogService.Close(),
                        nextAction: () =>
                        {
                            _userIgnoreActiveSwaps = true;
                            if (withAppUpdate)
                            {
                                OnUpdateClick();
                            }
                            else
                            {
                                _ = SignOut();
                            }
                        });

                    App.DialogService.Show(messageViewModel);
                    return;
                }

                _ = Dispatcher.UIThread.InvokeAsync(() => { App.DialogService.Close(); });

                _app.ChangeAtomexClient(atomexClient: null, account: null);
                _userIgnoreActiveSwaps = false;

                ShowStart();
            }
            catch (Exception e)
            {
                Log.Error(e, "Sign Out error");
            }
        }

        private async Task<bool> HasActiveSwapsAsync()
        {
            var swaps = await _app.Account
                .GetSwapsAsync();

            return swaps.Any(swap => swap.IsActive);
        }

        private async Task<bool> WhetherToCancelClosingAsync()
        {
            if (_app.Account == null) return false;
            if (!_app.Account.UserData.ShowActiveSwapWarning)
                return false;

            var hasActiveSwaps = await HasActiveSwapsAsync();

            return hasActiveSwaps;
        }

        private void InactivityHandler(object sender, EventArgs args)
        {
            if (_app?.Account == null)
                return;

            var pathToAccount = _app.Account.Wallet.PathToWallet;
            var accountDirectory = Path.GetDirectoryName(pathToAccount);

            if (accountDirectory == null)
                return;

            var accountName = new DirectoryInfo(accountDirectory).Name;
            
            _unlockViewModel = new UnlockViewModel(
                walletName: accountName,
                unlockAction: password =>
                {
                    var _ = Account.LoadFromFile(
                        pathToAccount: pathToAccount,
                        password: password,
                        currenciesProvider: _app.CurrenciesProvider);
                },
                goBack: async () => await SignOut(),
                onUnlock: async () =>
                {
                    ShowContent(WalletMainViewModel);
                    App.DialogService.UnlockWallet();

                    var userId = Atomex.ViewModels.Helpers.GetUserId(_app.Account);
                    var messages = await Atomex.ViewModels.Helpers.GetUserMessages(userId);
                    if (messages == null) return;
                    
                    foreach (var message in messages.Where(message => !message.IsReaded))
                    {
                        _ = Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            App.DialogService.Show(
                                MessageViewModel.Success(
                                    title: Resources.CvWarning,
                                    text: message.Message,
                                    nextAction: async () =>
                                    {
                                        await Atomex.ViewModels.Helpers.MarkUserMessageReaded(message.Id);
                                        App.DialogService.Close();
                                    }));
                        });
                    }
                });

            App.DialogService.LockWallet();
            ShowContent(_unlockViewModel);
        }

        public bool IsLinux { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        public void CloseDialog()
        {
            App.DialogService.Close();
        }

        private void DesignerMode()
        {
            HasAccount = true;
        }

        private void StartLookingForUserMessages(TimeSpan delayInterval)
        {
            var userId = Atomex.ViewModels.Helpers.GetUserId(_app.Account);
            var firstRun = true;

            _ = Task.Run(async () =>
            {
                while (_hasAccount)
                {
                    if (firstRun)
                    {
                        firstRun = false;
                    }
                    else
                    {
                        await Task.Delay(delayInterval);
                        if (!_hasAccount) return;
                    }

                    if (AccountRestored || Content is UnlockViewModel) continue;
                    var messages = await Atomex.ViewModels.Helpers.GetUserMessages(userId);
                    if (messages == null) continue;

                    foreach (var message in messages.Where(message => !message.IsReaded))
                    {
                        _ = Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            App.DialogService.Show(
                                MessageViewModel.Success(
                                    title: Resources.CvWarning,
                                    text: message.Message,
                                    nextAction: async () =>
                                    {
                                        await Atomex.ViewModels.Helpers.MarkUserMessageReaded(message.Id);
                                        App.DialogService.Close();
                                    }));
                        });

                        break;
                    }
                }
            });
        }
    }
}