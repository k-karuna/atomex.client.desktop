using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using Atomex.Client.Desktop.Common;
using Atomex.Client.Desktop.Properties;
using Atomex.Client.Desktop.ViewModels.Abstract;
using Atomex.Core;
using Atomex.MarketData.Abstract;
using Atomex.TezosTokens;
using Atomex.ViewModels;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Tezos;
using Atomex.Common;

namespace Atomex.Client.Desktop.ViewModels.SendViewModels
{
    public class TezosTokensSendViewModel : ViewModelBase
    {
        public static readonly string DefaultCurrencyFormat = $"F{AddressesHelper.MaxTokenCurrencyFormatDecimals}";
        public const string DefaultBaseCurrencyCode = "USD";
        public const string DefaultBaseCurrencyFormat = "$0.00";

        protected readonly IAtomexApp _app;
        [Reactive] public decimal SelectedFromBalance { get; set; }
        [Reactive] public string From { get; set; }
        [ObservableAsProperty] public string FromBeautified { get; }
        [Reactive] protected string TokenContract { get; set; }
        [ObservableAsProperty] public string TokenContractBeautified { get; }
        [Reactive] public int TokenId { get; set; }
        [Reactive] public string To { get; set; }
        public string TokenPreviewUrl { get; set; }
        private readonly string _tokenType;
        [Reactive] public string CurrencyFormat { get; set; }
        protected string FeeCurrencyFormat { get; set; }
        [Reactive] public string BaseCurrencyFormat { get; set; }
        [Reactive] protected decimal Amount { get; set; }
        [Reactive] protected decimal Fee { get; set; }
        [Reactive] public bool UseDefaultFee { get; set; }
        [Reactive] public decimal AmountInBase { get; set; }
        [Reactive] public decimal FeeInBase { get; set; }
        [Reactive] public string CurrencyCode { get; set; }
        [Reactive] public string FeeCurrencyCode { get; set; }
        public string BaseCurrencyCode { get; set; }
        [Reactive] public string? Warning { get; set; }
        [Reactive] public string? WarningToolTip { get; set; }
        [Reactive] public MessageType WarningType { get; set; }
        [Reactive] public bool ConfirmStage { get; set; }
        [Reactive] public bool CanSend { get; set; }
        [ObservableAsProperty] public bool IsSending { get; }

        public SelectAddressViewModel SelectFromViewModel { get; set; }
        public SelectAddressViewModel SelectToViewModel { get; set; }

        public TezosTokensSendViewModel()
        {
#if DEBUG
            if (Design.IsDesignMode)
                DesignerMode();
#endif
        }

        public TezosTokensSendViewModel(
            IAtomexApp app,
            string tokenContract,
            int tokenId,
            string tokenType,
            string tokenPreviewUrl,
            string? balanceFormat = null,
            string? from = null,
            bool showToSelectDialog = true)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));

            var tezosConfig = _app.Account
                .Currencies
                .Get<TezosConfig>(TezosConfig.Xtz);

            var updateCurrencyCodeCommand = ReactiveCommand.Create(UpdateCurrencyCode);
            var updateAmountCommand = ReactiveCommand.CreateFromTask(UpdateAmount);
            var updateFeeCommand = ReactiveCommand.CreateFromTask(UpdateFee);

            this.WhenAnyValue(
                    vm => vm.From,
                    vm => vm.To,
                    vm => vm.Amount,
                    vm => vm.Fee)
                .SubscribeInMainThread(_ => Warning = string.Empty);

            this.WhenAnyValue(vm => vm.From)
                .Select(s => s.TruncateAddress())
                .ToPropertyExInMainThread(this, vm => vm.FromBeautified);

            this.WhenAnyValue(vm => vm.TokenContract)
                .Select(s => s.TruncateAddress())
                .ToPropertyExInMainThread(this, vm => vm.TokenContractBeautified);

            this.WhenAnyValue(
                    vm => vm.From,
                    vm => vm.TokenId)
                .Select(_ => Unit.Default)
                .InvokeCommandInMainThread(updateCurrencyCodeCommand);

            this.WhenAnyValue(
                    vm => vm.Amount,
                    vm => vm.From,
                    vm => vm.To,
                    vm => vm.TokenId)
                .Select(_ => Unit.Default)
                .InvokeCommandInMainThread(updateAmountCommand);

            this.WhenAnyValue(
                    vm => vm.Fee,
                    vm => vm.From,
                    vm => vm.TokenId,
                    vm => vm.UseDefaultFee)
                .Select(_ => Unit.Default)
                .InvokeCommandInMainThread(updateFeeCommand);

            this.WhenAnyValue(
                    vm => vm.Amount,
                    vm => vm.Fee)
                .Subscribe(_ => OnQuotesUpdatedEventHandler(_app.QuotesProvider, EventArgs.Empty));

            this.WhenAnyValue(
                    vm => vm.Amount,
                    vm => vm.To,
                    vm => vm.Warning,
                    vm => vm.WarningType,
                    vm => vm.ConfirmStage)
                .Throttle(TimeSpan.FromMilliseconds(1))
                .SubscribeInMainThread(_ =>
                {
                    if (!ConfirmStage)
                    {
                        CanSend = To != null &&
                                  Amount > 0 &&
                                  (string.IsNullOrEmpty(Warning) ||
                                   (Warning != null && WarningType != MessageType.Error));
                    }
                    else
                    {
                        CanSend = true;
                    }
                });

            NextCommand
                .IsExecuting
                .ToPropertyExInMainThread(this, vm => vm.IsSending);

            CurrencyCode = string.Empty;
            FeeCurrencyCode = TezosConfig.Xtz;
            BaseCurrencyCode = DefaultBaseCurrencyCode;

            FeeCurrencyFormat = tezosConfig.FeeFormat;
            BaseCurrencyFormat = DefaultBaseCurrencyFormat;

            TokenContract = tokenContract;

            TokenId = tokenId;
            _tokenType = tokenType;
            TokenPreviewUrl = tokenPreviewUrl;

            if (from != null)
            {
                From = from;
                Amount = SelectedFromBalance;
            }

            UpdateCurrencyCode();
            SubscribeToServices();
            UseDefaultFee = true;

            SelectFromViewModel = new SelectAddressViewModel(
                _app.Account,
                tezosConfig,
                SelectAddressMode.SendFrom,
                from,
                tokenId,
                tokenContract)
            {
                BackAction = () => { App.DialogService.Show(this); },
                ConfirmAction = walletAddressViewModel =>
                {
                    TokenId = walletAddressViewModel.TokenId;
                    From = walletAddressViewModel.Address;
                    App.DialogService.Show(showToSelectDialog ? SelectToViewModel! : this);
                }
            };

            SelectToViewModel = new SelectAddressViewModel(_app.Account, tezosConfig)
            {
                BackAction = () => { App.DialogService.Show(SelectFromViewModel); },
                ConfirmAction = walletAddressViewModel =>
                {
                    To = walletAddressViewModel.Address;
                    App.DialogService.Show(this);
                }
            };
        }

        private void SubscribeToServices()
        {
            if (_app.HasQuotesProvider)
                _app.QuotesProvider.QuotesUpdated += OnQuotesUpdatedEventHandler;
        }

        private ReactiveCommand<Unit, Unit>? _backCommand;

        public ReactiveCommand<Unit, Unit> BackCommand => _backCommand ??= (_backCommand = ReactiveCommand.Create(() =>
        {
            App.DialogService.Close();
        }));

        private ReactiveCommand<Unit, Unit>? _nextCommand;

        public ReactiveCommand<Unit, Unit> NextCommand =>
            _nextCommand ??= ReactiveCommand.CreateFromTask(OnNextCommand);

        private ReactiveCommand<Unit, Unit>? _maxCommand;
        public ReactiveCommand<Unit, Unit> MaxCommand => _maxCommand ??= ReactiveCommand.Create(OnMaxClick);

        private ReactiveCommand<Unit, Unit>? _undoConfirmStageCommand;

        public ReactiveCommand<Unit, Unit> UndoConfirmStageCommand => _undoConfirmStageCommand ??=
            (_undoConfirmStageCommand = ReactiveCommand.Create(() => { ConfirmStage = false; }));

        private ReactiveCommand<Unit, Unit>? _selectFromCommand;

        public ReactiveCommand<Unit, Unit> SelectFromCommand => _selectFromCommand ??=
            (_selectFromCommand = ReactiveCommand.Create(FromClick));

        private ReactiveCommand<Unit, Unit>? _selectToCommand;

        public ReactiveCommand<Unit, Unit> SelectToCommand => _selectToCommand ??=
            (_selectToCommand = ReactiveCommand.Create(ToClick));

        private void FromClick()
        {
            SelectFromViewModel.ConfirmAction = walletAddressViewModel =>
            {
                TokenId = walletAddressViewModel.TokenId;
                From = walletAddressViewModel.Address;
                App.DialogService.Show(this);
            };

            SelectFromViewModel.BackAction = () => App.DialogService.Show(this);
            App.DialogService.Show(SelectFromViewModel);
        }

        private void ToClick()
        {
            SelectToViewModel.BackAction = () => App.DialogService.Show(this);
            App.DialogService.Show(SelectToViewModel);
        }

        private async Task OnNextCommand()
        {
            var tezosConfig = _app.Account
                .Currencies
                .Get<TezosConfig>(TezosConfig.Xtz);

            if (string.IsNullOrEmpty(To))
            {
                Warning = Resources.SvEmptyAddressError;
                return;
            }

            if (!tezosConfig.IsValidAddress(To))
            {
                Warning = Resources.SvInvalidAddressError;
                WarningType = MessageType.Error;
                return;
            }

            if (Amount <= 0)
            {
                Warning = Resources.SvAmountLessThanZeroError;
                WarningType = MessageType.Error;
                return;
            }

            if (Fee <= 0)
            {
                Warning = Resources.SvCommissionLessThanZeroError;
                WarningType = MessageType.Error;
                return;
            }

            if (TokenContract == null || From == null)
            {
                Warning = "Invalid 'From' address or token contract address!";
                WarningType = MessageType.Error;
                return;
            }

            if (!tezosConfig.IsValidAddress(TokenContract))
            {
                Warning = "Invalid token contract address!";
                WarningType = MessageType.Error;
                return;
            }

            var fromTokenAddress = await GetTokenAddressAsync(
                account: _app.Account,
                address: From,
                tokenContract: TokenContract,
                tokenId: TokenId,
                tokenType: _tokenType);

            if (fromTokenAddress == null)
            {
                Warning = $"Insufficient token funds on address {From}! Please update your balance!";
                return;
            }

            if (Amount > fromTokenAddress.Balance)
            {
                Warning = $"Insufficient token funds on address {fromTokenAddress.Address}! " +
                          $"Please use Max button to find out how many tokens you can send!";
                return;
            }

            var xtzAddress = await _app.Account
                .GetAddressAsync(TezosConfig.Xtz, From);

            if (xtzAddress == null)
            {
                Warning = $"Insufficient funds for fee. Please update your balance for address {From}!";
                return;
            }

            if (xtzAddress.AvailableBalance() < Fee)
            {
                Warning = "Insufficient funds for fee!";
                return;
            }

            if (ConfirmStage)
            {
                try
                {
                    var error = await Send();

                    if (error != null)
                    {
                        App.DialogService.Show(MessageViewModel.Error(
                            text: error.Description,
                            backAction: () => App.DialogService.Show(this)));

                        return;
                    }

                    App.DialogService.Show(MessageViewModel.Success(
                        text: "Sending was successful",
                        nextAction: () => { App.DialogService.Close(); }));
                }
                catch (Exception e)
                {
                    App.DialogService.Show(MessageViewModel.Error(
                        text: "An error has occurred while sending transaction.",
                        backAction: () => App.DialogService.Show(this)));

                    Log.Error(e, "Tezos tokens transaction send error");
                }
                finally
                {
                    ConfirmStage = false;
                }
            }

            ConfirmStage = true;
        }

        private async Task UpdateAmount()
        {
            try
            {
                var tezosConfig = _app.Account
                    .Currencies
                    .Get<TezosConfig>(TezosConfig.Xtz);

                if (From == null)
                {
                    Warning = Resources.SvInvalidFromAddress;
                    WarningToolTip = "";
                    WarningType = MessageType.Error;
                }

                if (TokenContract == null || !tezosConfig.IsValidAddress(TokenContract))
                {
                    Warning = Resources.SvInvalidTokenContract;
                    WarningToolTip = "";
                    WarningType = MessageType.Error;
                    return;
                }

                var fromTokenAddress = await GetTokenAddressAsync(
                    account: _app.Account,
                    address: From!,
                    tokenContract: TokenContract,
                    tokenId: TokenId,
                    tokenType: _tokenType);

                if (fromTokenAddress == null)
                {
                    Warning = Resources.CvInsufficientFunds;
                    WarningToolTip = "";
                    WarningType = MessageType.Error;
                    return;
                }

                if (Amount > fromTokenAddress.Balance)
                {
                    Warning = Resources.CvInsufficientFunds;
                    WarningToolTip = Resources.CvBigAmount;
                    WarningType = MessageType.Error;
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Tezos tokens update amount error");
            }
        }

        private async Task UpdateFee()
        {
            try
            {
                var tezosConfig = _app.Account
                    .Currencies
                    .Get<TezosConfig>(TezosConfig.Xtz);

                if (From == null)
                {
                    Warning = Resources.SvInvalidFromAddress;
                    WarningToolTip = "";
                    WarningType = MessageType.Error;
                }

                if (TokenContract == null || !tezosConfig.IsValidAddress(TokenContract))
                {
                    Warning = Resources.SvInvalidTokenContract;
                    WarningToolTip = "";
                    WarningType = MessageType.Error;
                    return;
                }

                if (UseDefaultFee)
                {
                    var fromTokenAddress = await GetTokenAddressAsync(
                        account: _app.Account,
                        address: From!,
                        tokenContract: TokenContract,
                        tokenId: TokenId,
                        tokenType: _tokenType);

                    if (fromTokenAddress == null)
                    {
                        Warning = Resources.CvInsufficientFunds;
                        WarningToolTip = "";
                        WarningType = MessageType.Error;
                        return;
                    }

                    var tokenAccount = _app.Account
                        .GetTezosTokenAccount<TezosTokenAccount>(fromTokenAddress.Currency, TokenContract, TokenId);

                    var (estimatedFee, isEnougth) = await tokenAccount
                        .EstimateTransferFeeAsync(From);

                    if (!isEnougth)
                    {
                        Warning = string.Format(Resources.SvInsufficientChainFundsWithDetails, "XTZ", estimatedFee);
                        WarningToolTip = "";
                        WarningType = MessageType.Error;
                        return;
                    }

                    Fee = estimatedFee;
                }
                else
                {
                    var xtzAddress = await _app.Account
                        .GetAddressAsync(TezosConfig.Xtz, From);

                    if (xtzAddress == null)
                    {
                        Warning = string.Format(Resources.CvInsufficientChainFunds, "XTZ");
                        WarningToolTip = "";
                        WarningType = MessageType.Error;
                        return;
                    }

                    Fee = Math.Min(Fee, tezosConfig.GetMaximumFee());

                    if (xtzAddress.AvailableBalance() < Fee)
                    {
                        Warning = string.Format(Resources.CvInsufficientChainFunds, "XTZ");
                        WarningToolTip = "";
                        WarningType = MessageType.Error;
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Tezos tokens update fee error");
            }
        }

        protected async void OnMaxClick()
        {
            try
            {
                var tezosConfig = _app.Account
                    .Currencies
                    .Get<TezosConfig>(TezosConfig.Xtz);

                if (From == null)
                {
                    Amount = 0;
                    return;
                }

                if (TokenContract == null || !tezosConfig.IsValidAddress(TokenContract))
                {
                    Warning = Resources.SvInvalidTokenContract;
                    WarningToolTip = "";
                    WarningType = MessageType.Error;
                    Amount = 0;
                    return;
                }

                var fromTokenAddress = await GetTokenAddressAsync(
                    account: _app.Account,
                    address: From,
                    tokenContract: TokenContract,
                    tokenId: TokenId,
                    tokenType: _tokenType);

                if (fromTokenAddress == null)
                {
                    Warning = Resources.CvInsufficientFunds;
                    WarningToolTip = "";
                    WarningType = MessageType.Error;
                    Amount = 0;
                    return;
                }

                Amount = fromTokenAddress.Balance;
            }
            catch (Exception e)
            {
                Log.Error(e, "Tezos tokens max click error");
            }
        }

        private void OnQuotesUpdatedEventHandler(object? sender, EventArgs args)
        {
            if (sender is not IQuotesProvider quotesProvider)
                return;

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                AmountInBase = !string.IsNullOrEmpty(CurrencyCode)
                    ? Amount.SafeMultiply(quotesProvider.GetQuote(CurrencyCode, BaseCurrencyCode)?.Bid ?? 0m)
                    : 0;

                FeeInBase = !string.IsNullOrEmpty(FeeCurrencyCode)
                    ? Fee.SafeMultiply(quotesProvider.GetQuote(FeeCurrencyCode, BaseCurrencyCode)?.Bid ?? 0m)
                    : 0;
            });
        }

        public static async Task<WalletAddress> GetTokenAddressAsync(
            IAccount account,
            string address,
            string tokenContract,
            decimal tokenId,
            string tokenType)
        {
            var tezosAccount = account
                .GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz);

            return await tezosAccount
                .DataRepository
                .GetTezosTokenAddressAsync(tokenType, tokenContract, tokenId, address);
        }

        private async void UpdateCurrencyCode()
        {
            if (TokenContract == null || From == null)
                return;

            var tokenAddress = await GetTokenAddressAsync(
                account: _app.Account,
                address: From,
                tokenContract: TokenContract,
                tokenId: TokenId,
                tokenType: _tokenType);

            if (tokenAddress?.TokenBalance?.Symbol != null)
            {
                CurrencyCode = tokenAddress.TokenBalance.Symbol;
                CurrencyFormat =
                    $"F{Math.Min(tokenAddress.TokenBalance.Decimals, AddressesHelper.MaxTokenCurrencyFormatDecimals)}";
            }
            else
            {
                var currencyCode = _app.Account.Currencies
                    .FirstOrDefault(c => c is Fa12Config fa12 && fa12.TokenContractAddress == TokenContract)
                    ?.Name;

                CurrencyFormat = DefaultCurrencyFormat;
                
                if (currencyCode == null)
                {
                    CurrencyCode = string.Empty;
                    CurrencyFormat = "F0";
                }
            }

            SelectedFromBalance = tokenAddress?.AvailableBalance() ?? 0;
            this.RaisePropertyChanged(nameof(Amount));
        }

        private async Task<Error> Send(CancellationToken cancellationToken = default)
        {
            var tokenAddress = await GetTokenAddressAsync(
                account: App.AtomexApp.Account,
                address: From,
                tokenContract: TokenContract,
                tokenId: TokenId,
                tokenType: _tokenType);

            var currencyName = App.AtomexApp.Account.Currencies
                .FirstOrDefault(c => c is TezosTokenConfig tokenConfig &&
                                     tokenConfig.TokenContractAddress == TokenContract &&
                                     tokenConfig.TokenId == TokenId)
                ?.Name ?? _tokenType;

            var tokenAccount = App.AtomexApp.Account.GetTezosTokenAccount<TezosTokenAccount>(
                currency: currencyName,
                tokenContract: TokenContract,
                tokenId: TokenId);

            var (_, error) = await tokenAccount.SendAsync(
                from: tokenAddress.Address,
                to: To,
                amount: Amount,
                fee: Fee,
                useDefaultFee: UseDefaultFee,
                cancellationToken: cancellationToken);

            return error;
        }

#if DEBUG
        private void DesignerMode()
        {
        }
#endif
    }
}