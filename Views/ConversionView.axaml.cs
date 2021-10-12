using System;
using System.Reactive.Linq;
using Atomex.Client.Desktop.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Atomex.Client.Desktop.Views
{
    public class ConversionView : UserControl
    {
        public ConversionView()
        {
            InitializeComponent();
            
            var amountStringTextBox = this.FindControl<TextBox>("AmountString");

            amountStringTextBox.GetObservable(TextBox.TextProperty)
                .Throttle(TimeSpan.FromMilliseconds(1))
                .Subscribe(text =>
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (DataContext is ConversionViewModel viewModel)
                            viewModel.AmountString = text;
                    });
                });
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}