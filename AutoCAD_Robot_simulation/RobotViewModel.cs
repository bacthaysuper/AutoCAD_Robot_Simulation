using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Speech.Recognition;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.Geometry;

namespace AutoCAD_Robot_simulation
{
    public static class SharedData
    {
        public static RobotArmParameters Config { get; set; } = new();
        public static Point3d AiPick { get; set; }
        public static Point3d AiPlace { get; set; }
    }

    public class RelayCommand(Action execute, Func<bool> canExecute = null) : ICommand
    {
        private readonly Action _execute = execute;
        private readonly Func<bool> _canExecute = canExecute;

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object parameter) => _execute();
    }

    [SupportedOSPlatform("windows")]
    public class RobotViewModel : INotifyPropertyChanged
    {
        private const string IdleColor = "#1ABC9C";
        private const string ListeningColor = "#E74C3C";
        private const float ConfidenceThreshold = 0.15f;
        private static readonly string[] ActionKeywords = ["move", "pick", "place", "take", "put", "the", "payload", "box", "to", "at", "and", "minus"];
        private static readonly string[] AxisKeywords = ["x", "y", "z"];
        private static readonly string[] NumberOnes = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];
        private static readonly string[] NumberTens = ["zero", "ten", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

        private readonly Dispatcher _uiDispatcher = Dispatcher.CurrentDispatcher;

        private string _status = "Status: Waiting for action...";
        private string _aiCommandText;
        private string _micButtonText = "[MIC] Activate Voice";
        private string _micButtonColor = IdleColor;
        private bool _isAiBusy;
        private bool _isListening;
        private SpeechRecognitionEngine _recognizer;

        public double LowerArmLength
        {
            get => SharedData.Config.LowerArmSize.Z;
            set { var current = SharedData.Config.LowerArmSize; SharedData.Config.LowerArmSize = new(current.X, current.Y, value); OnPropertyChanged(); }
        }

        public double UpperArmLength
        {
            get => SharedData.Config.UpperArmSize.Z;
            set { var current = SharedData.Config.UpperArmSize; SharedData.Config.UpperArmSize = new(current.X, current.Y, value); OnPropertyChanged(); }
        }

        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        public string AiCommandText { get => _aiCommandText; set { _aiCommandText = value; OnPropertyChanged(); } }
        public string MicButtonText { get => _micButtonText; set { _micButtonText = value; OnPropertyChanged(); } }
        public string MicButtonColor { get => _micButtonColor; set { _micButtonColor = value; OnPropertyChanged(); } }

        public ICommand PickPlaceCommand { get; }
        public ICommand SendAiCommand { get; }
        public ICommand ToggleMicCommand { get; }

        public RobotViewModel()
        {
            PickPlaceCommand = new RelayCommand(() => SafeSendAutoCADCommand("PICK_AND_PLACE "));
            SendAiCommand = new RelayCommand(async () => await ExecuteAiCommandAsync(), () => !_isAiBusy);
            ToggleMicCommand = new RelayCommand(ToggleMic);
        }

        private bool InitializeSpeechEngine()
        {
            try
            {
                _recognizer = new SpeechRecognitionEngine(new CultureInfo("en-US"));

                var keywords = new Choices();
                keywords.Add(ActionKeywords);
                keywords.Add(AxisKeywords); 

                var numberWords = new Choices();
                for (int i = 0; i <= 350; i += 10)
                {
                    numberWords.Add(NumberToWords(i));
                }
                keywords.Add(numberWords);

                var commandGrammar = new Grammar(new GrammarBuilder(keywords, 1, 20)) { Name = "RobotCommand" };
                var dictationGrammar = new DictationGrammar { Name = "Dictation" };

                _recognizer.LoadGrammar(commandGrammar);
                _recognizer.LoadGrammar(dictationGrammar);

                _recognizer.SetInputToDefaultAudioDevice();
                _recognizer.SpeechRecognized += Recognizer_SpeechRecognized;
                _recognizer.SpeechRecognitionRejected += Recognizer_SpeechRecognitionRejected;

                return true;
            }
            catch (Exception ex)
            {
                Status = $"Status: Speech Engine Error - {ex.Message}";
                return false;
            }
        }

        private static string NumberToWords(int number)
        {
            if (number < 20) return NumberOnes[number];

            if (number < 100)
            {
                int t = number / 10, o = number % 10;
                return o == 0 ? NumberTens[t] : $"{NumberTens[t]} {NumberOnes[o]}";
            }

            int hundreds = number / 100;
            int remainder = number % 100;
            string hundredPart = $"{NumberOnes[hundreds]} hundred";
            return remainder == 0 ? hundredPart : $"{hundredPart} {NumberToWords(remainder)}";
        }

        private void ToggleMic()
        {
            if (_recognizer == null && !InitializeSpeechEngine()) return;

            if (_isListening)
            {
                _recognizer.RecognizeAsyncCancel();
                _isListening = false;
                MicButtonText = "[MIC] Activate Voice";
                MicButtonColor = IdleColor;
                Status = "Status: Microphone off.";
            }
            else
            {
                _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                _isListening = true;
                MicButtonText = "[MIC] Stop Listening";
                MicButtonColor = ListeningColor;
                Status = "Status: Listening... Speak now!";
            }
        }

        private void Recognizer_SpeechRecognitionRejected(object sender, SpeechRecognitionRejectedEventArgs e)
        {
            try
            {
                _uiDispatcher.Invoke(() =>
                {
                    Status = "Status: Heard some noise, but couldn't understand English.";
                });
            }
            catch (Exception ex)
            {
                LogSafely($"RecognitionRejected handler error: {ex.Message}");
            }
        }

        private void Recognizer_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            try
            {
                _uiDispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        ToggleMic();

                        if (e.Result.Confidence < ConfidenceThreshold)
                        {
                            Status = $"Status: Ignored (Confidence too low: {e.Result.Confidence:F2})";
                            return;
                        }

                        AiCommandText = e.Result.Text;
                        Status = $"Status: Heard ({e.Result.Confidence:F2}) -> \"{AiCommandText}\"";

                        await ExecuteAiCommandAsync();
                    }
                    catch (Exception ex)
                    {
                        Status = $"Status: Recognition handling error - {ex.Message}";
                    }
                }));
            }
            catch (Exception ex)
            {
                LogSafely($"SpeechRecognized handler error: {ex.Message}");
            }
        }

        private static void LogSafely(string message)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "robot_speech_error.log"),
                    $"{DateTime.Now}: {message}{Environment.NewLine}");
            }
            catch { }
        }

        private async Task ExecuteAiCommandAsync()
        {
            if (string.IsNullOrWhiteSpace(AiCommandText) || _isAiBusy) return;

            _isAiBusy = true;
            _uiDispatcher.Invoke(() => CommandManager.InvalidateRequerySuggested());
            Status = "Status: Analyzing prompt via Gemini AI...";

            try
            {
                var task = await AI.GetCoordinatesFromTextAsync(AiCommandText);

                _uiDispatcher.Invoke(() =>
                {
                    SharedData.AiPick = ClampCoordinate(task.Pick.X, task.Pick.Y, task.Pick.Z);
                    SharedData.AiPlace = ClampCoordinate(task.Place.X, task.Place.Y, task.Place.Z);
                    Status = $"Status: AI SUCCESS -> Pick({task.Pick.X}, {task.Pick.Y}) | Place({task.Place.X}, {task.Place.Y})";

                    SafeSendAutoCADCommand("EXECUTE_AI_TASK ");
                });
            }
            catch (Exception ex)
            {
                _uiDispatcher.Invoke(() => Status = $"Status: AI ERROR - {ex.Message}");
            }
            finally
            {
                _uiDispatcher.Invoke(() =>
                {
                    _isAiBusy = false;
                    CommandManager.InvalidateRequerySuggested();
                });
            }
        }

        private void SafeSendAutoCADCommand(string cmd)
        {
            _uiDispatcher.InvokeAsync(() =>
            {
                void Handler(object s, EventArgs e)
                {
                    try
                    {
                        Application.Idle -= Handler;

                        var doc = Application.DocumentManager.MdiActiveDocument;
                        doc?.SendStringToExecute(cmd, true, false, false);
                    }
                    catch { }
                }

                Application.Idle += Handler;
            });
        }
        private const double MaxPhysicalReach = 280;
        private static Point3d ClampCoordinate(double x, double y, double z)
        {
            double distance = Math.Sqrt((x * x) + (y * y) + (z * z));

            if (distance <= MaxPhysicalReach || distance == 0)
                return new Point3d(x, y, z);

            double scale = MaxPhysicalReach / distance;
            return new Point3d(x * scale, y * scale, z * scale);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}