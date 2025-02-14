﻿using System;
using System.Windows.Input;
using ReactiveUI;
using Serilog;
using Atomex.Client.Desktop.Properties;

namespace Atomex.Client.Desktop.ViewModels
{
    public class MessageViewModel : ViewModelBase
    {
        private readonly Action? _backAction;
        private readonly Action? _nextAction;

        public string Title { get; }
        public string Text { get; }
        public string BaseUrl { get; }
        public string Id { get; }
        public string TransactionUri => $"{BaseUrl}{Id}";
        public string NextText { get; }
        public bool IsBackVisible { get; }
        public bool IsLinkVisible { get; }
        public bool IsNextVisible { get; }
        public bool WithProgressBar { get; }


        private ICommand _backCommand;

        public ICommand BackCommand =>
            _backCommand ??= (_backCommand = ReactiveCommand.Create(() => { _backAction?.Invoke(); }));

        private ICommand _nextCommand;

        public ICommand NextCommand =>
            _nextCommand ??= (_nextCommand = ReactiveCommand.Create(() => { _nextAction?.Invoke(); }));

        public MessageViewModel()
        {
        }

        public MessageViewModel(
            string title,
            string text,
            string nextTitle,
            Action? backAction,
            Action? nextAction,
            bool withProgressBar = false)
        {
            Title = title;
            Text = text;
            NextText = nextTitle;

            IsBackVisible = backAction != null;
            IsNextVisible = !string.IsNullOrEmpty(NextText);
            WithProgressBar = withProgressBar;

            _backAction = backAction;
            _nextAction = nextAction;
        }

        public MessageViewModel(
            string title,
            string text,
            string baseUrl,
            string id,
            string nextTitle,
            Action? backAction,
            Action? nextAction,
            bool withProgressBar = false)
        {
            Title = title;
            Text = text;

            NextText = nextTitle;
            BaseUrl = baseUrl;
            Id = id;

            IsLinkVisible = !string.IsNullOrEmpty(BaseUrl) && !string.IsNullOrEmpty(Id);
            IsBackVisible = backAction != null;
            IsNextVisible = !string.IsNullOrEmpty(NextText);
            WithProgressBar = withProgressBar;

            _backAction = backAction;
            _nextAction = nextAction;
        }

        public static MessageViewModel Error(string text, Action backAction) =>
            new(
                title: Resources.SvError,
                text: text,
                nextTitle: null,
                backAction: backAction,
                nextAction: null);

        public static MessageViewModel Success(string text, Action nextAction) =>
            new(
                title: Resources.SvSuccess,
                text: text,
                nextTitle: Resources.SvOk,
                backAction: null,
                nextAction: nextAction);

        public static MessageViewModel Success(string title, string text, Action nextAction) =>
            new(
                title: title,
                text: text,
                nextTitle: Resources.SvOk,
                backAction: null,
                nextAction: nextAction);

        public static MessageViewModel Message(string title, bool withProgressBar) =>
            new(
                title: title,
                text: null,
                nextTitle: null,
                backAction: null,
                nextAction: null,
                withProgressBar: withProgressBar
            );

        public static MessageViewModel Message(string title, string text, Action nextAction, string buttonTitle,
            bool withProgressBar) =>
            new(
                title: title,
                text: text,
                nextTitle: buttonTitle,
                backAction: null,
                nextAction: nextAction,
                withProgressBar: withProgressBar
            );

        public static MessageViewModel Message(string title, string text, Action backAction) =>
            new(
                title: title,
                text: text,
                nextTitle: null,
                backAction: backAction,
                nextAction: null);

        public static MessageViewModel Message(string title, string text, string nextTitle, Action backAction,
            Action nextAction) =>
            new(
                title: title,
                text: text,
                nextTitle: nextTitle,
                backAction: backAction,
                nextAction: nextAction);

        public static MessageViewModel Message(string title, string text, string nextTitle, Action nextAction) => new(
            title: title,
            text: text,
            nextTitle: nextTitle,
            backAction: null,
            nextAction: nextAction);

        public static MessageViewModel Success(string text, string baseUrl, string id, Action nextAction) =>
            new(
                title: Resources.SvSuccess,
                text: text,
                baseUrl: baseUrl,
                id: id,
                nextTitle: Resources.SvOk,
                backAction: null,
                nextAction: nextAction);

        private ICommand _openTxInExplorerCommand;

        public ICommand OpenTxInExplorerCommand => _openTxInExplorerCommand ??= (_openTxInExplorerCommand =
            ReactiveCommand.Create(() =>
            {
                if (Uri.TryCreate(TransactionUri, UriKind.Absolute, out var uri))
                    App.OpenBrowser(uri.ToString());
                else
                    Log.Error("Invalid uri for transaction explorer");
            }));

        private ICommand _copyCommand;

        public ICommand CopyCommand => _copyCommand ??= (_copyCommand = ReactiveCommand.Create<string>((s) =>
        {
            try
            {
                App.Clipboard.SetTextAsync(s);
            }
            catch (Exception e)
            {
                Log.Error(e, "Copy to clipboard error");
            }
        }));
    }
}