using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace AutoCAD_Robot_simulation
{
    public class RobotAnimator(Document doc)
    {
        private readonly Editor _editor = doc.Editor;

        public static void RotatePart(Transaction tr, ObjectId partId, double deltaAngleRadian, Vector3d axis, Point3d pivot)
        {
            if (tr.GetObject(partId, OpenMode.ForWrite) is Solid3d part)
            {
                part.TransformBy(Matrix3d.Rotation(deltaAngleRadian, axis, pivot));
            }
        }

        public static void TranslatePart(Transaction tr, ObjectId partId, Vector3d translation)
        {
            if (tr.GetObject(partId, OpenMode.ForWrite) is Entity ent)
            {
                ent.TransformBy(Matrix3d.Displacement(translation));
            }
        }

        public void UpdateScreenFrame(int delayMilliseconds = 50)
        {
            _editor.UpdateScreen();
            Thread.Sleep(delayMilliseconds);
        }
    }
}