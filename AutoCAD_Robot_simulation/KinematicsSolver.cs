using System;
using Autodesk.AutoCAD.Geometry;

namespace AutoCAD_Robot_simulation
{
    public class KinematicsSolver(double lowerArmLength, double upperArmLength)
    {
        private readonly double _l1 = lowerArmLength;
        private readonly double _l2 = upperArmLength;

        public double[] CalculateAngles(Point3d target, Point3d origin)
        {
            double x = target.Y - origin.Y;
            double z = target.Z - origin.Z;

            double distanceSquare = (x * x) + (z * z);
            double cosTheta2 = (distanceSquare - (_l1 * _l1) - (_l2 * _l2)) / (2 * _l1 * _l2);

            if (cosTheta2 > 1.0 || cosTheta2 < -1.0)
            {
                throw new Exception("Target point is out of reach!");
            }

            double theta2 = -Math.Acos(cosTheta2);
            double k1 = _l1 + (_l2 * Math.Cos(theta2));
            double k2 = _l2 * Math.Sin(theta2);
            double theta1 = Math.Atan2(z, x) - Math.Atan2(k2, k1);

            return [theta1, theta2];
        }
    }
}