using System;
using Autodesk.AutoCAD.Geometry;

namespace AutoCAD_Robot_simulation
{
    public readonly struct ArmAngles(double baseYaw, double shoulder, double elbow)
    {
        public double BaseYaw { get; } = baseYaw;
        public double Shoulder { get; } = shoulder;
        public double Elbow { get; } = elbow;
    }

    public class KinematicsSolver(double lowerArmLength, double upperArmLength)
    {
        private readonly double _l1 = lowerArmLength;
        private readonly double _l2 = upperArmLength;

        public ArmAngles CalculateAngles(Point3d target, Point3d origin)
        {
            double dx = target.X - origin.X;
            double dy = target.Y - origin.Y;
            double dz = target.Z - origin.Z;
            double baseYaw = Math.Atan2(dy, dx);

            double reach = Math.Sqrt((dx * dx) + (dy * dy));
            double distanceSquare = (reach * reach) + (dz * dz);

            double cosElbow = (distanceSquare - (_l1 * _l1) - (_l2 * _l2)) / (2 * _l1 * _l2);
            cosElbow = Math.Max(-1.0, Math.Min(1.0, cosElbow));
            double elbow = Math.Acos(cosElbow);

            double k1 = _l1 + (_l2 * Math.Cos(elbow));
            double k2 = _l2 * Math.Sin(elbow);
            double shoulderMath = Math.Atan2(dz, reach) + Math.Atan2(k2, k1);
            double shoulder = (Math.PI / 2) - shoulderMath;

            return new ArmAngles(baseYaw, shoulder, elbow);
        }
    }
}