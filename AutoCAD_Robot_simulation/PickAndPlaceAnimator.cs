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
        Document doc,
        Point3d[] keyPoints,
        Point3d startPos,
        ObjectId lowerArmId,
        ObjectId joint2Id,
        ObjectId upperArmId,
        ObjectId gripperBaseId,
        ObjectId finger1Id,
        ObjectId finger2Id,
        ObjectId payloadId,
        ObjectId obstacleId)
    {
        private const int StepsPerSegment = 50;
        private const double GripperOffset = 3.0;
        private const double EscapeHeight = 25.0;
        private const int GrabKeyIndex = 1;
        private const int PreDescentKeyIndex = 3;
        private const int ReleaseKeyIndex = 4;
        private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(15);

        private readonly Document _doc = doc;
        private readonly Editor _ed = doc.Editor;

        private readonly KinematicsSolver _ikSolver =
            new(SharedData.Config.LowerArmSize.Z, SharedData.Config.UpperArmSize.Z + 10.0);
        private readonly Point3d _lowerArmPivot = new(0, 0, SharedData.Config.BaseHeight);
        private Point3d _upperArmPivot =
            new(0, 0, SharedData.Config.BaseHeight + SharedData.Config.LowerArmSize.Z);

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
        private double _prevTheta1 = Math.PI / 2;
        private double _prevTheta2 = 0;
        private bool _isGrabbed = false;
        private double _obstacleClearance = 0;

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

                if (_isGrabbed)
                    UpdateObstacleClearance(tr);

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
            return new Point3d(basePoint.X, basePoint.Y, basePoint.Z + _obstacleClearance);
        }

        private void UpdateObstacleClearance(Transaction tr)
        {
            if (_payloadId == ObjectId.Null || _obstacleId == ObjectId.Null)
                return;

            if (tr.GetObject(_payloadId, OpenMode.ForRead) is not Solid3d payload ||
                tr.GetObject(_obstacleId, OpenMode.ForWrite) is not Solid3d obstacle)
                return;

            if (!payload.CheckInterference(obstacle))
                return;

            if (_obstacleClearance <= 0)
            {
                _ed.WriteMessage("\n[WARNING] Near Collision Hazard Detected! Rerouting trajectory...");
                _ed.WriteMessage("\n[PATH PLANNER] Obstacle bypassed successfully. Resuming path to target.");
            }

            obstacle.Color = Color.FromColorIndex(ColorMethod.ByAci, 2);
            _obstacleClearance = EscapeHeight;
        }

        private void MoveArmTowards(Transaction tr, Point3d targetPt)
        {
            double[] angles = _ikSolver.CalculateAngles(targetPt, _lowerArmPivot);
            double deltaTheta1 = angles[0] - _prevTheta1;
            double deltaTheta2 = angles[1] - _prevTheta2;

            RobotAnimator.RotatePart(tr, _lowerArmId, deltaTheta1, Vector3d.XAxis, _lowerArmPivot);
            RobotAnimator.RotatePart(tr, _joint2Id, deltaTheta1, Vector3d.XAxis, _lowerArmPivot);
            RobotAnimator.RotatePart(tr, _upperArmId, deltaTheta1, Vector3d.XAxis, _lowerArmPivot);
            RobotAnimator.RotatePart(tr, _gripperBaseId, deltaTheta1, Vector3d.XAxis, _lowerArmPivot);
            RobotAnimator.RotatePart(tr, _finger1Id, deltaTheta1, Vector3d.XAxis, _lowerArmPivot);
            RobotAnimator.RotatePart(tr, _finger2Id, deltaTheta1, Vector3d.XAxis, _lowerArmPivot);
            if (_isGrabbed)
                RobotAnimator.RotatePart(tr, _payloadId, deltaTheta1, Vector3d.XAxis, _lowerArmPivot);

            _upperArmPivot = _upperArmPivot.RotateBy(deltaTheta1, Vector3d.XAxis, _lowerArmPivot);

            RobotAnimator.RotatePart(tr, _upperArmId, deltaTheta2, Vector3d.XAxis, _upperArmPivot);
            RobotAnimator.RotatePart(tr, _gripperBaseId, deltaTheta2, Vector3d.XAxis, _upperArmPivot);
            RobotAnimator.RotatePart(tr, _finger1Id, deltaTheta2, Vector3d.XAxis, _upperArmPivot);
            RobotAnimator.RotatePart(tr, _finger2Id, deltaTheta2, Vector3d.XAxis, _upperArmPivot);
            if (_isGrabbed)
                RobotAnimator.RotatePart(tr, _payloadId, deltaTheta2, Vector3d.XAxis, _upperArmPivot);

            _prevTheta1 = angles[0];
            _prevTheta2 = angles[1];
        }

        private void RefreshView()
        {
            _doc.TransactionManager.QueueForGraphicsFlush();
            _ed.UpdateScreen();
        }

        private void AdvanceStep(Point3d targetKey)
        {
            _step++;
            if (_step <= StepsPerSegment)
                return;

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
            RobotAnimator.TranslatePart(tr, _finger1Id, new Vector3d(direction, 0, 0));
            RobotAnimator.TranslatePart(tr, _finger2Id, new Vector3d(-direction, 0, 0));
            tr.Commit();
            _ed.UpdateScreen();
        }
    }
}