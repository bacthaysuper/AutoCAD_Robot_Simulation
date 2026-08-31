using System;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace AutoCAD_Robot_simulation
{
    [SupportedOSPlatform("windows")]
    public partial class RobotControlView : UserControl
    {
        private bool _isListening;
        private bool _webViewReady;

        public RobotControlView()
        {
            InitializeComponent();
            Loaded += RobotControlView_Loaded;
        }

        private async void RobotControlView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_webViewReady || DataContext is not RobotViewModel vm)
                return;

            try
            {
                await SpeechWebView.EnsureCoreWebView2Async();
                SpeechWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                SpeechWebView.NavigateToString(SpeechHtml);
                _webViewReady = true;
            }
            catch (Exception ex)
            {
                vm.Status = $"Status: Voice engine failed to load - {ex.Message}";
            }
        }

        private async void MicButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_webViewReady || DataContext is not RobotViewModel vm)
                return;

            if (_isListening)
            {
                await SpeechWebView.ExecuteScriptAsync("stopRecognition()");
            }
            else
            {
                vm.Status = "Status: Initializing microphone...";
                await SpeechWebView.ExecuteScriptAsync("startRecognition()");
            }
        }

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (DataContext is not RobotViewModel vm)
                return;

            string json = e.TryGetWebMessageAsString();
            var message = JsonSerializer.Deserialize<VoiceMessage>(json);

            switch (message?.Type)
            {
                case "started":
                    _isListening = true;
                    vm.MicButtonText = "[MIC] Listening... Click to stop";
                    vm.Status = "Status: Listening for voice command...";
                    break;

                case "result":
                    vm.AiCommandText = message.Text;
                    vm.Status = $"Status: Voice recognized -> \"{message.Text}\"";
                    break;

                case "stopped":
                    _isListening = false;
                    vm.MicButtonText = "[MIC] Activate Voice";
                    vm.Status = "Status: Waiting for action...";
                    break;

                case "error":
                    _isListening = false;
                    vm.MicButtonText = "[MIC] Activate Voice";
                    vm.Status = $"Status: Voice error - {message.Message}";
                    break;
            }
        }

        private class VoiceMessage
        {
            public string Type { get; set; }
            public string Text { get; set; }
            public string Message { get; set; }
        }

        private const string SpeechHtml = @"
<!DOCTYPE html>
<html>
<body>
<script>
    let recognition;

    function startRecognition() {
        try {
            const SpeechRecognitionApi = window.SpeechRecognition || window.webkitSpeechRecognition;
            if (!SpeechRecognitionApi) {
                window.chrome.webview.postMessage(JSON.stringify({ type: 'error', message: 'Speech recognition not supported.' }));
                return;
            }

            recognition = new SpeechRecognitionApi();
            recognition.lang = 'en-US';
            recognition.continuous = false;
            recognition.interimResults = false;

            recognition.onstart = function () {
                window.chrome.webview.postMessage(JSON.stringify({ type: 'started' }));
            };

            recognition.onresult = function (event) {
                const text = event.results[0][0].transcript;
                window.chrome.webview.postMessage(JSON.stringify({ type: 'result', text: text }));
            };

            recognition.onerror = function (event) {
                window.chrome.webview.postMessage(JSON.stringify({ type: 'error', message: event.error }));
            };

            recognition.onend = function () {
                window.chrome.webview.postMessage(JSON.stringify({ type: 'stopped' }));
            };

            recognition.start();
        } catch (ex) {
            window.chrome.webview.postMessage(JSON.stringify({ type: 'error', message: ex.message }));
        }
    }

    function stopRecognition() {
        if (recognition) {
            recognition.stop();
        }
    }
</script>
</body>
</html>";
    }
}