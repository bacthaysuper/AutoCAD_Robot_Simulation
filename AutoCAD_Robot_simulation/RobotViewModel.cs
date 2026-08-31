using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows.Input;
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
        private const double MinReach = -350;
        private const double MaxReach = 350;

        private string _status = "Status: Waiting for action...";
        private string _aiCommandText;
        private string _micButtonText = "[MIC] Activate Voice";
        private bool _isAiBusy;

        public double LowerArmLength
        {
            get => SharedData.Config.LowerArmSize.Z;
            set
            {
                var current = SharedData.Config.LowerArmSize;
                SharedData.Config.LowerArmSize = new(current.X, current.Y, value);
                OnPropertyChanged();
            }
        }

        public double UpperArmLength
        {
            get => SharedData.Config.UpperArmSize.Z;
            set
            {
                var current = SharedData.Config.UpperArmSize;
                SharedData.Config.UpperArmSize = new(current.X, current.Y, value);
                OnPropertyChanged();
            }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string AiCommandText
        {
            get => _aiCommandText;
            set { _aiCommandText = value; OnPropertyChanged(); }
        }

        public string MicButtonText
        {
            get => _micButtonText;
            set { _micButtonText = value; OnPropertyChanged(); }
        }

        public ICommand PickPlaceCommand { get; }
        public ICommand SendAiCommand { get; }

        public RobotViewModel()
        {
            PickPlaceCommand = new RelayCommand(() => SendAutoCADCommand("PICK_AND_PLACE "));
            SendAiCommand = new RelayCommand(async () => await ExecuteAiCommandAsync(), () => !_isAiBusy);
        }

        private async Task ExecuteAiCommandAsync()
        {
            if (string.IsNullOrWhiteSpace(AiCommandText) || _isAiBusy)
                return;

            _isAiBusy = true;
            CommandManager.InvalidateRequerySuggested();
            Status = "Status: Analyzing prompt via Gemini AI...";

            try
            {
                var task = await AI.GetCoordinatesFromTextAsync(AiCommandText);

                SharedData.AiPick = ClampCoordinate(task.Pick.X, task.Pick.Y, task.Pick.Z);
                SharedData.AiPlace = ClampCoordinate(task.Place.X, task.Place.Y, task.Place.Z);

                Status = $"Status: AI SUCCESS -> Pick({task.Pick.X}, {task.Pick.Y}) | Place({task.Place.X}, {task.Place.Y})";

                Application.DocumentManager.MdiActiveDocument?.SendStringToExecute("EXECUTE_AI_TASK ", true, false, false);
            }
            catch (Exception ex)
            {
                Status = $"Status: AI ERROR - {ex.Message}";
            }
            finally
            {
                _isAiBusy = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private static Point3d ClampCoordinate(double x, double y, double z)
        {
            return new Point3d(
                Math.Clamp(x, MinReach, MaxReach),
                Math.Clamp(y, MinReach, MaxReach),
                Math.Clamp(z, MinReach, MaxReach));
        }

        private static void SendAutoCADCommand(string cmd)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            doc.Window.Focus();
            doc.SendStringToExecute(cmd, true, false, false);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}