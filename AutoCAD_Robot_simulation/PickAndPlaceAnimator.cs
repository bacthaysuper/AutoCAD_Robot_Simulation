using System;
using System.Windows.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace AutoCAD_Robot_simulation
{
    public class PickAndPlaceAnimator(
        Document doc, Point3d[] keyPoints, Point3d startPos,
        ObjectId lowerArmId, ObjectId joint2Id, ObjectId upperArmId,
        ObjectId gripperBaseId, ObjectId finger1Id, ObjectId finger2Id,
        ObjectId payloadId, ObjectId obstacleId)
    {
        private const int StepsPerSegment = 50;
        private const double GripperOffset = 3.0;
        private const double EscapeHeight = 25.0;
        private const int GrabKeyIndex = 1;
        private const int PreDescentKeyIndex = 3;
        private const int ReleaseKeyIndex = 4;
        private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(15);
        private static readonly Point3d YawPivot = Point3d.Origin;

        private readonly Document _doc = doc;
        private readonly Editor _ed = doc.Editor;

        private readonly KinematicsSolver _ikSolver = new(SharedData.Config.LowerArmSize.Z, SharedData.Config.UpperArmSize.Z + 10.0);
        private readonly Point3d _lowerArmPivot = new(0, 0, SharedData.Config.BaseHeight);
        private readonly Point3d _restUpperArmPivot = new(0, 0, SharedData.Config.BaseHeight + SharedData.Config.LowerArmSize.Z);

        private readonly ObjectId _lowerArmId = lowerArmId;
        private readonly ObjectId _joint2Id = joint2Id;
        private readonly ObjectId _upperArmId = upperArmId;
        private readonly ObjectId _gripperBaseId = gripperBaseId;
        private readonly ObjectId _finger1Id = finger1Id;
        private readonly ObjectId _finger2Id = finger2Id;
        private readonly ObjectId _payloadId = payloadId;
        private readonly ObjectId _obstacleId = obstacleId;

        private readonly Point3d[] _keyPoints = keyPoints;
        private Point3d _currentPos = startPos;
        private int _keyIndex = 0;
        private int _step = 1;
        private bool _isGrabbed = false;
        private double _obstacleClearance = 0;
        private double _currentClearance = 0;
        private Matrix3d _lowerTotal = Matrix3d.Identity;
        private Matrix3d _upperTotal = Matrix3d.Identity;
        private DispatcherTimer _timer;

        public event Action Completed;
        public event Action<string> Failed;

        public void Start()
        {
            _timer = new DispatcherTimer { Interval = TickInterval };
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
        }

        public void Stop() => _timer?.Stop();

        private void Tick()
        {
            if (_keyIndex >= _keyPoints.Length)
            {
                _timer.Stop();
                Completed?.Invoke();
                return;
            }

            using var docLock = _doc.LockDocument();
            using var tr = _doc.Database.TransactionManager.StartTransaction();

            try
            {
                Point3d targetKey = _keyPoints[_keyIndex];

                if (_isGrabbed) UpdateObstacleClearance(tr);

                Point3d targetPt = InterpolateStep(targetKey);
                MoveArmTowards(tr, targetPt);

                tr.Commit();
                RefreshView();
                AdvanceStep(targetKey);
            }
            catch (Exception ex)
            {
                _timer.Stop();
                Failed?.Invoke(ex.Message);
            }
        }

        private Point3d InterpolateStep(Point3d targetKey)
        {
            double t = _step / (double)StepsPerSegment;
            double smoothT = (t * t) * (3.0 - 2.0 * t);
            Point3d basePoint = _currentPos + ((targetKey - _currentPos) * smoothT);
            _currentClearance += (_obstacleClearance - _currentClearance) * 0.2;
            return new Point3d(basePoint.X, basePoint.Y, basePoint.Z + _currentClearance);
        }

        private void UpdateObstacleClearance(Transaction tr)
        {
            if (_payloadId == ObjectId.Null || _obstacleId == ObjectId.Null) return;

            if (tr.GetObject(_payloadId, OpenMode.ForRead) is not Solid3d payload ||
                tr.GetObject(_obstacleId, OpenMode.ForWrite) is not Solid3d obstacle) return;

            if (!payload.CheckInterference(obstacle)) return;

            if (_obstacleClearance <= 0)
            {
                _ed.WriteMessage("\n[WARNING] Near Collision Hazard Detected! Rerouting trajectory...");
            }

            obstacle.Color = Color.FromColorIndex(ColorMethod.ByAci, 2);
            _obstacleClearance = EscapeHeight;
        }

        private void MoveArmTowards(Transaction tr, Point3d targetPt)
        {
            ArmAngles angles = _ikSolver.CalculateAngles(targetPt, _lowerArmPivot);
            Vector3d bendAxis = Vector3d.YAxis.RotateBy(angles.BaseYaw, Vector3d.ZAxis);

            Matrix3d yawMatrix = Matrix3d.Rotation(angles.BaseYaw, Vector3d.ZAxis, YawPivot);
            Matrix3d shoulderMatrix = Matrix3d.Rotation(angles.Shoulder, bendAxis, _lowerArmPivot);
            Matrix3d newLowerTotal = shoulderMatrix * yawMatrix;

            Point3d currentUpperArmPivot = _restUpperArmPivot.TransformBy(newLowerTotal);
            Matrix3d elbowMatrix = Matrix3d.Rotation(angles.Elbow, bendAxis, currentUpperArmPivot);
            Matrix3d newUpperTotal = elbowMatrix * newLowerTotal;

            Matrix3d lowerDelta = newLowerTotal * _lowerTotal.Inverse();
            Matrix3d upperDelta = newUpperTotal * _upperTotal.Inverse();

            ApplyTransform(tr, _lowerArmId, lowerDelta);
            ApplyTransform(tr, _joint2Id, lowerDelta);
            ApplyTransform(tr, _upperArmId, upperDelta);
            ApplyTransform(tr, _gripperBaseId, upperDelta);
            ApplyTransform(tr, _finger1Id, upperDelta);
            ApplyTransform(tr, _finger2Id, upperDelta);

            if (_isGrabbed) ApplyTransform(tr, _payloadId, upperDelta);

            _lowerTotal = newLowerTotal;
            _upperTotal = newUpperTotal;
        }

        private static void ApplyTransform(Transaction tr, ObjectId id, Matrix3d transform)
        {
            if (id == ObjectId.Null) return;
            if (tr.GetObject(id, OpenMode.ForWrite) is Entity ent) ent.TransformBy(transform);
        }

        private void RefreshView()
        {
            _doc.TransactionManager.QueueForGraphicsFlush();
            _ed.Regen();
        }

        private void AdvanceStep(Point3d targetKey)
        {
            _step++;
            if (_step <= StepsPerSegment) return;

            _currentPos = targetKey;
            _step = 1;

            if (_keyIndex == GrabKeyIndex)
            {
                GripperMove(GripperOffset);
                _isGrabbed = true;
            }
            else if (_keyIndex == PreDescentKeyIndex)
            {
                _obstacleClearance = 0;
            }
            else if (_keyIndex == ReleaseKeyIndex)
            {
                _isGrabbed = false;
                GripperMove(-GripperOffset);
            }

            _keyIndex++;
        }

        private void GripperMove(double direction)
        {
            using var tr = _doc.Database.TransactionManager.StartTransaction();
            Vector3d localMove1 = new Vector3d(direction, 0, 0).TransformBy(_upperTotal);
            Vector3d localMove2 = new Vector3d(-direction, 0, 0).TransformBy(_upperTotal);

            if (_finger1Id != ObjectId.Null && tr.GetObject(_finger1Id, OpenMode.ForWrite) is Entity f1)
                f1.TransformBy(Matrix3d.Displacement(localMove1));

            if (_finger2Id != ObjectId.Null && tr.GetObject(_finger2Id, OpenMode.ForWrite) is Entity f2)
                f2.TransformBy(Matrix3d.Displacement(localMove2));

            tr.Commit();
            _ed.UpdateScreen();
        }
    }
}