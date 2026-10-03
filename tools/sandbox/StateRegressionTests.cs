using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Desktop_Frames.FarmFences;

namespace FarmFenceSandbox
{
    internal static class StateRegressionTests
    {
        public static async Task Run(Action<string> log)
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "state-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "farm-fences.json");
            FenceManager? manager = null;
            try
            {
                manager = new FenceManager(file, arrangeDesktop: false);
                await manager.Ready;
                Require(manager.Fences.Count == 0, "缺少設定時必須零個柵欄");
                await manager.CreateNewFenceAsync("公文 A");
                await manager.CreateNewFenceAsync("捷徑 B");
                await manager.CreateNewFenceAsync("資料夾 C");
                var original = manager.Fences[0].GetPhysicalBounds();
                var expected = new Rect(original.Left, original.Top, 320, 280);
                manager.Fences[0].SetPhysicalBounds(expected);
                manager.SaveState();
                manager.Stop();
                string saved = File.ReadAllText(file, Encoding.UTF8);
                manager.Stop();
                Require(File.ReadAllText(file, Encoding.UTF8) == saved, "重複退出不得清空保存資料");
                manager = new FenceManager(file, arrangeDesktop: false);
                await manager.Ready;
                Require(manager.Fences.Count == 3 && manager.Fences[0].FenceTitle == "公文 A", "三個柵欄與名稱必須恢復");
                Require(manager.Fences[0].GetPhysicalBounds() == expected, "實體座標與尺寸必須恢復");
                manager.Stop();
                log("PASS：零／多個柵欄、名稱、實體座標、尺寸、重啟與重複退出。");

                // 舊原型沒有 ItemPositions 仍可讀取；新增資料不得重複歸屬。
                var legacy = JsonSerializer.Deserialize<List<FenceStateData>>("[{\"Id\":\"A\",\"Title\":\"舊柵欄\",\"Width\":320,\"Height\":280,\"AssignedItems\":[]}]")!;
                FenceManager.ValidateStates(legacy);
                var duplicate = new List<FenceStateData> {
                    new() { Id="A", Title="A", Width=320, Height=280, AssignedItems=new() {"same"}},
                    new() { Id="B", Title="B", Width=320, Height=280, AssignedItems=new() {"SAME"}}
                };
                bool rejected = false;
                try { FenceManager.ValidateStates(duplicate); } catch (InvalidDataException) { rejected = true; }
                Require(rejected, "重複歸屬必須拒絕");
                log("PASS：舊原型資料相容、單一歸屬防護。");

                const string broken = "[故意損毀的測試資料";
                File.WriteAllText(file, broken, new UTF8Encoding(false));
                manager = new FenceManager(file, arrangeDesktop: false);
                int warnings = 0;
                manager.Warning += _ => warnings++;
                await manager.Ready;
                await manager.CreateNewFenceAsync("不得覆寫");
                manager.SaveState();
                manager.Stop();
                Require(warnings > 0 && File.ReadAllText(file, Encoding.UTF8) == broken, "毀損設定必須警示且保留原檔");
                File.WriteAllText(file, "[]", new UTF8Encoding(false));
                manager = new FenceManager(file, arrangeDesktop: false);
                await manager.Ready;
                Require(manager.Fences.Count == 0, "保存的空陣列不得產生預設柵欄");
                manager.Stop();
                log("PASS：讀取失敗禁止覆寫、零個柵欄保存與恢復。");

                var overlap = new List<FenceStateData> {
                    new() { Id="A", Title="舊框 A", Left=80, Top=80, Width=320, Height=280, AssignedItems=new() {"原歸屬一"},
                        ItemPositions=new() { ["原歸屬一"] = new ItemPlacement { X=100, Y=130 } } },
                    new() { Id="B", Title="舊框 B", Left=100, Top=100, Width=320, Height=280, AssignedItems=new() {"原歸屬二"},
                        ItemPositions=new() { ["原歸屬二"] = new ItemPlacement { X=140, Y=150 } } }
                };
                File.WriteAllText(file, JsonSerializer.Serialize(overlap), new UTF8Encoding(false));
                manager = new FenceManager(file, arrangeDesktop: false);
                warnings = 0;
                manager.Warning += _ => warnings++;
                await manager.Ready;
                Require(warnings > 0 && manager.Fences.Count == 2, "既有重疊先提示，不清除外框");
                manager.SaveState();
                var protectedStates = JsonSerializer.Deserialize<List<FenceStateData>>(File.ReadAllText(file, Encoding.UTF8))!;
                Require(protectedStates[0].AssignedItems.SequenceEqual(overlap[0].AssignedItems) &&
                    protectedStates[1].AssignedItems.SequenceEqual(overlap[1].AssignedItems) &&
                    protectedStates[0].ItemPositions["原歸屬一"].X == 100, "既有重疊不交換歸屬或覆寫位置");
                while (manager.Fences.Count > 0)
                {
                    var removed = manager.Fences[0];
                    manager.CancelFence(removed);
                    removed.Close();
                    var cancelled = JsonSerializer.Deserialize<List<FenceStateData>>(File.ReadAllText(file, Encoding.UTF8))!;
                    Require(cancelled.Count == manager.Fences.Count && cancelled.All(s => s.Id != removed.FenceId), "取消只解除指定外框歸屬");
                }
                manager.Stop();
                Require(JsonSerializer.Deserialize<List<FenceStateData>>(File.ReadAllText(file, Encoding.UTF8))!.Count == 0, "取消全部保存空清單");
                log("PASS：既有重疊提示與保留歸屬、取消一個／全部不清空其他外框。");
            }
            finally
            {
                manager?.Stop();
                foreach (string generated in new[] { file, file + ".tmp" })
                    if (File.Exists(generated)) File.Delete(generated);
                Directory.Delete(folder); // 僅本次建立的空測試目錄，不遞迴刪除。
            }
        }

        private static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }
    }
}
