using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using System.Drawing;
using System.Runtime.Versioning;

namespace AutoCAD_Robot_simulation
{
    [SupportedOSPlatform("windows")]
    public class UIManager
    {
        private static PaletteSet _ps;

        [CommandMethod("ROBOT_UI")]
        public static void ShowRobotUI()
        {
            if (_ps == null)
            {
                _ps = new("ROBOT CONTROL PANEL")
                {
                    Style = PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowPropertiesMenu,
                    MinimumSize = new Size(500, 700),
                    DockEnabled = DockSides.None
                };

                var wpfPanel = new RobotControlView();
                _ps.AddVisual("Menu", wpfPanel);

                _ps.Size = new Size(500, 700);
                _ps.Location = new Point(200, 200);
            }

            _ps.Visible = true;
        }
    }
}