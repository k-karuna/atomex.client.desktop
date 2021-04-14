using System;
using System.Collections.Generic;
using System.Linq;
using Atomex.Client.Desktop.Common;
using Atomex.Client.Desktop.Properties;
using NBitcoin;

namespace Atomex.Client.Desktop.ViewModels.CreateWalletViewModels
{
    public class WriteMnemonicViewModel : StepViewModel
    {
        private static IEnumerable<KeyValuePair<string, Wordlist>> Languages { get; } =
            new List<KeyValuePair<string, Wordlist>>
            {
                new KeyValuePair<string, Wordlist>("English", Wordlist.English),
                new KeyValuePair<string, Wordlist>("French", Wordlist.French),
                new KeyValuePair<string, Wordlist>("Japanese", Wordlist.Japanese),
                new KeyValuePair<string, Wordlist>("Spanish", Wordlist.Spanish),
                new KeyValuePair<string, Wordlist>("Portuguese Brazil", Wordlist.PortugueseBrazil),
                new KeyValuePair<string, Wordlist>("Chinese Traditional", Wordlist.ChineseTraditional),
                new KeyValuePair<string, Wordlist>("Chinese Simplified", Wordlist.ChineseSimplified)
            };

        public IEnumerable<string> GetLanguages
        {
            get => Languages.Select(kv => kv.Key).ToList();
        }

        private StepData StepData { get; set; }
        
        public string LanguageString
        {
            get => KeyValExtension.GetKeyByValue(Languages, Language);
            set
            {
                Language = KeyValExtension.GetValueByKey(Languages, value);
            }
        }
        
        private Wordlist _language = Wordlist.English;
        private Wordlist Language
        {
            get => _language;
            set
            {
                if (_language != value) {
                    _language = value;
                    Mnemonic = string.Empty;
                }
            }
        }

        private string _mnemonic;

        public string Mnemonic
        {
            get => _mnemonic;
            set
            {
                _mnemonic = value;
                OnPropertyChanged(nameof(Mnemonic));
            }
        }

        private string _warning;

        public string Warning
        {
            get => _warning;
            set
            {
                _warning = value;
                OnPropertyChanged(nameof(Warning));
            }
        }

        public override void Initialize(
            object arg)
        {
            StepData = (StepData) arg;
        }

        public override void Back()
        {
            Warning = string.Empty;

            base.Back();
        }

        public override void Next()
        {
            if (string.IsNullOrEmpty(Mnemonic))
            {
                Warning = Resources.CwvMnemonicIsEmptyError;
                return;
            }

            try
            {
                var unused = new Mnemonic(Mnemonic, Language);
            }
            catch (Exception e)
            {
                if (e.Message.Contains("Word count should be"))
                    Warning = Resources.CwvMnemonicInvalidWordcountError;
                else if (e.Message.Contains("is not in the wordlist"))
                    Warning = Resources.CwvMnemonicInvalidWordError.Replace("{0}", $"\"{e.Message.Split(' ')[1]}\"");
                else
                    Warning = Resources.CwvMnemonicInvalidError;

                return;
            }

            StepData.Mnemonic = Mnemonic;
            StepData.Language = Language;

            RaiseOnNext(StepData);
        }
    }
}