using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AutoCAD_Robot_simulation
{
    public class RobotArmParameters
    {
        public double BaseHeight { get; set; } = 30.0;
        public Vector3d LowerArmSize { get; set; } = new(20.0, 16.0, 162.0);
        public Vector3d UpperArmSize { get; set; } = new(16.0, 12.0, 130.0);
    }

    public class RobotArmBuilder(RobotArmParameters config)
    {
        private readonly RobotArmParameters _config = config;

        public List<Solid3d> BuildRobotArm()
        {
            return
            [
                CreateIndustrialBase(),
                CreateMotorJoint(new(0, 0, _config.BaseHeight)),
                CreateIndustrialLink(_config.LowerArmSize.Z, 5.0, 4.0, new(0, 0, _config.BaseHeight)),
                CreateMotorJoint(new(0, 0, _config.BaseHeight + _config.LowerArmSize.Z)),
                CreateIndustrialLink(_config.UpperArmSize.Z, 4.0, 3.5, new(0, 0, _config.BaseHeight + _config.LowerArmSize.Z)),
                CreateGripperBase(new(0, 0, _config.BaseHeight + _config.LowerArmSize.Z + _config.UpperArmSize.Z)),
                CreateFinger(new(3, 0, _config.BaseHeight + _config.LowerArmSize.Z + _config.UpperArmSize.Z + 2), false),
                CreateFinger(new(-3, 0, _config.BaseHeight + _config.LowerArmSize.Z + _config.UpperArmSize.Z + 2), true)
            ];
        }

        private Solid3d CreateIndustrialBase()
        {
            var baseSolid = new Solid3d();
            baseSolid.CreateFrustum(_config.BaseHeight * 0.7, 6.0, 6.0, 6.0);

            var basePlate = new Solid3d();
            basePlate.CreateBox(16.0, 16.0, _config.BaseHeight * 0.3);
            basePlate.TransformBy(Matrix3d.Displacement(new(0, 0, -(_config.BaseHeight * 0.35))));

            baseSolid.BooleanOperation(BooleanOperationType.BoolUnite, basePlate);
            baseSolid.TransformBy(Matrix3d.Displacement(new(0, 0, _config.BaseHeight * 0.35)));
            baseSolid.Color = Color.FromColorIndex(ColorMethod.ByAci, 250);

            return baseSolid;
        }

        private static Solid3d CreateMotorJoint(Point3d position)
        {
            var joint = new Solid3d();
            joint.CreateFrustum(7.0, 3.5, 3.5, 3.5);
            joint.TransformBy(Matrix3d.Rotation(Math.PI / 2, Vector3d.YAxis, Point3d.Origin));
            joint.TransformBy(Matrix3d.Displacement(position.GetAsVector()));
            joint.Color = Color.FromColorIndex(ColorMethod.ByAci, 250);

            return joint;
        }

        private static Solid3d CreateIndustrialLink(double length, double width, double thickness, Point3d startPoint)
        {
            var link = new Solid3d();
            link.CreateBox(thickness, width, length);
            link.TransformBy(Matrix3d.Displacement(new(0, 0, length / 2)));

            var bottomFillet = new Solid3d();
            bottomFillet.CreateFrustum(thickness, width / 2, width / 2, width / 2);
            bottomFillet.TransformBy(Matrix3d.Rotation(Math.PI / 2, Vector3d.YAxis, Point3d.Origin));

            var topFillet = new Solid3d();
            topFillet.CreateFrustum(thickness, width / 2.5, width / 2.5, width / 2.5);
            topFillet.TransformBy(Matrix3d.Rotation(Math.PI / 2, Vector3d.YAxis, Point3d.Origin));
            topFillet.TransformBy(Matrix3d.Displacement(new(0, 0, length)));

            link.BooleanOperation(BooleanOperationType.BoolUnite, bottomFillet);
            link.BooleanOperation(BooleanOperationType.BoolUnite, topFillet);

            link.TransformBy(Matrix3d.Displacement(startPoint.GetAsVector()));
            link.Color = Color.FromColorIndex(ColorMethod.ByAci, 44);

            return link;
        }

        private static Solid3d CreateGripperBase(Point3d position)
        {
            var baseSolid = new Solid3d();
            baseSolid.CreateFrustum(3.0, 2.5, 2.5, 2.5);
            baseSolid.TransformBy(Matrix3d.Displacement(new(0, 0, 1.5)));

            var flange = new Solid3d();
            flange.CreateBox(8.0, 4.0, 1.5);
            flange.TransformBy(Matrix3d.Displacement(new(0, 0, 3.75)));

            baseSolid.BooleanOperation(BooleanOperationType.BoolUnite, flange);
            baseSolid.TransformBy(Matrix3d.Displacement(position.GetAsVector()));
            baseSolid.Color = Color.FromColorIndex(ColorMethod.ByAci, 250);

            return baseSolid;
        }

        private static Solid3d CreateFinger(Point3d position, bool isMirrored)
        {
            var finger = new Solid3d();
            finger.CreateBox(2.0, 4.0, 6.0);
            finger.TransformBy(Matrix3d.Displacement(new(0, 0, 3.0)));

            var tip = new Solid3d();
            tip.CreateBox(4.0, 4.0, 1.5);
            double dir = isMirrored ? 1.0 : -1.0;
            tip.TransformBy(Matrix3d.Displacement(new(dir, 0, 6.75)));

            finger.BooleanOperation(BooleanOperationType.BoolUnite, tip);
            finger.TransformBy(Matrix3d.Displacement(position.GetAsVector()));
            finger.Color = Color.FromColorIndex(ColorMethod.ByAci, 250);

            return finger;
        }

        public static Solid3d CreatePayload(Point3d position)
        {
            var payload = new Solid3d();
            payload.CreateBox(4.0, 4.0, 4.0);
            payload.TransformBy(Matrix3d.Displacement(position.GetAsVector() + new Vector3d(0, 0, 2.0)));
            payload.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);

            return payload;
        }

        public static Solid3d CreateBin(Point3d position)
        {
            var bin = new Solid3d();
            bin.CreateBox(12.0, 12.0, 4.0);
            bin.TransformBy(Matrix3d.Displacement(position.GetAsVector() + new Vector3d(0, 0, 2.0)));

            var innerHole = new Solid3d();
            innerHole.CreateBox(10.0, 10.0, 4.0);
            innerHole.TransformBy(Matrix3d.Displacement(position.GetAsVector() + new Vector3d(0, 0, 3.0)));

            bin.BooleanOperation(BooleanOperationType.BoolSubtract, innerHole);
            bin.Color = Color.FromColorIndex(ColorMethod.ByAci, 5);

            return bin;
        }
        public static Solid3d CreateObstacle(Point3d pPick, Point3d pPlace)
        {
            var wall = new Solid3d();
            wall.CreateBox(2.0, 25.0, 30.0);

            Point3d midPt = new((pPick.X + pPlace.X) / 2, (pPick.Y + pPlace.Y) / 2, 15.0);
            wall.TransformBy(Matrix3d.Displacement(midPt.GetAsVector()));

            double angle = Math.Atan2(pPlace.Y - pPick.Y, pPlace.X - pPick.X);
            wall.TransformBy(Matrix3d.Rotation(angle + (Math.PI / 2), Vector3d.ZAxis, midPt));

            wall.Color = Color.FromColorIndex(ColorMethod.ByAci, 40);

            return wall;
        }
    }
}