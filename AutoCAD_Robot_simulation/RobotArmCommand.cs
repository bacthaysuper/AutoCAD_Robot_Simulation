using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Runtime.Versioning;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoCAD_Robot_simulation
{
    [SupportedOSPlatform("windows")]
    public class RobotArmCommand
    {
        private const string RobotLayerName = "ROBOT_ARM";
        private const double PayloadHalfHeight = 2.0;
        private const double BinFloorHeight = 1.0;

        private static void EnsureRobotLayer(Database db, Transaction tr)
        {
            if (tr.GetObject(db.LayerTableId, OpenMode.ForRead) is not LayerTable lt || lt.Has(RobotLayerName))
                return;

            lt.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = RobotLayerName,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 3)
            };
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        private static BlockTableRecord GetModelSpace(Database db, Transaction tr)
        {
            var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
            return tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;
        }

        private static ObjectId AppendAndConfigureEntity(Transaction tr, BlockTableRecord ms, Solid3d ent)
        {
            ent.Layer = RobotLayerName;
            ObjectId id = ms.AppendEntity(ent);
            tr.AddNewlyCreatedDBObject(ent, true);
            return id;
        }

        private static void SetRealisticVisualStyle(Database db, Transaction tr)
        {
            var visualStyles = (DBDictionary)tr.GetObject(db.VisualStyleDictionaryId, OpenMode.ForRead);
            if (!visualStyles.Contains("Realistic")) return;

            var vt = (ViewportTable)tr.GetObject(db.ViewportTableId, OpenMode.ForRead);
            var vtr = (ViewportTableRecord)tr.GetObject(vt["*Active"], OpenMode.ForWrite);
            vtr.VisualStyleId = visualStyles.GetAt("Realistic");
        }

        [CommandMethod("PICK_AND_PLACE")]
        public static void PickAndPlaceCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;

            PromptPointResult pprPick = ed.GetPoint("\n>>> Select PICK point: ");
            if (pprPick.Status != PromptStatus.OK) return;

            PromptPointResult pprPlace = ed.GetPoint("\n>>> Select PLACE point: ");
            if (pprPlace.Status != PromptStatus.OK) return;

            Point3d pPick = new(pprPick.Value.X, pprPick.Value.Y, pprPick.Value.Z);
            Point3d pPlace = new(pprPlace.Value.X, pprPlace.Value.Y, pprPlace.Value.Z);

            RunSimulation(pPick, pPlace);
        }

        public static void RunSimulation(Point3d pPick, Point3d pPlace)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            ObjectId lowerArmId = ObjectId.Null, joint2Id = ObjectId.Null, upperArmId = ObjectId.Null;
            ObjectId gripperBaseId = ObjectId.Null, finger1Id = ObjectId.Null, finger2Id = ObjectId.Null;
            ObjectId payloadId, obstacleId;

            using (var docLock = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockTableRecord ms = GetModelSpace(db, tr);
                    var builder = new RobotArmBuilder(SharedData.Config);
                    var robotParts = builder.BuildRobotArm();

                    Solid3d payload = RobotArmBuilder.CreatePayload(pPick);
                    Solid3d targetBin = RobotArmBuilder.CreateBin(pPlace);
                    Solid3d obstacle = RobotArmBuilder.CreateObstacle(pPick, pPlace);

                    EnsureRobotLayer(db, tr);

                    for (int i = 0; i < robotParts.Count; i++)
                    {
                        ObjectId id = AppendAndConfigureEntity(tr, ms, robotParts[i]);
                        switch (i)
                        {
                            case 2: lowerArmId = id; break;
                            case 3: joint2Id = id; break;
                            case 4: upperArmId = id; break;
                            case 5: gripperBaseId = id; break;
                            case 6: finger1Id = id; break;
                            case 7: finger2Id = id; break;
                        }
                    }

                    payloadId = AppendAndConfigureEntity(tr, ms, payload);
                    AppendAndConfigureEntity(tr, ms, targetBin);
                    obstacleId = AppendAndConfigureEntity(tr, ms, obstacle);

                    SetRealisticVisualStyle(db, tr);
                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[SIMULATION ERROR]: {ex.Message}");
                    return;
                }
            }

            ed.WriteMessage("\n>>> Starting Pick and Place with Obstacle Avoidance...");

            double safeHoverZ = Math.Max(pPick.Z, pPlace.Z) + 20.0;
            Point3d pGrabTarget = new(pPick.X, pPick.Y, pPick.Z + PayloadHalfHeight);
            Point3d pDropTarget = new(pPlace.X, pPlace.Y, pPlace.Z + BinFloorHeight + 0.5);

            Point3d[] keyPoints =
            [
                new(pPick.X, pPick.Y, safeHoverZ),
                pGrabTarget,
                new(pPick.X, pPick.Y, safeHoverZ),
                new(pPlace.X, pPlace.Y, safeHoverZ),
                pDropTarget,
                new(pPlace.X, pPlace.Y, safeHoverZ)
            ];

            Point3d startPos = new(0, 0, SharedData.Config.BaseHeight + SharedData.Config.LowerArmSize.Z + SharedData.Config.UpperArmSize.Z + 10.0);

            var animator = new PickAndPlaceAnimator(doc, keyPoints, startPos,
                lowerArmId, joint2Id, upperArmId, gripperBaseId, finger1Id, finger2Id, payloadId, obstacleId);

            animator.Completed += () => ed.WriteMessage("\n>>> Simulation completed!");
            animator.Failed += msg => ed.WriteMessage($"\n[ANIMATION ERROR]: {msg}");

            animator.Start();
        }

        [CommandMethod("EXECUTE_AI_TASK")]
        public static void ExecuteAiTask()
        {
            RunSimulation(SharedData.AiPick, SharedData.AiPlace);
        }
    }
}