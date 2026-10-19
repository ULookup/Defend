using System;
using System.Collections.Generic;
using System.IO;
using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>只保存稳定关卡标识的通关标记，不保存局内建筑、资金或波次。</summary>
    public sealed class CampaignProgressStore
    {
        private readonly string prefix;
        private readonly Func<string, int> read;
        private readonly Action<string, int> write;
        private readonly Action save;
        private readonly CampaignProgressFile file;
        private readonly HashSet<string> pending = new HashSet<string>(StringComparer.Ordinal);
        public int PendingCount => pending.Count;
        public int Revision { get; private set; }
        public bool LastSaveFailed { get; private set; }
        public bool Loaded => file == null || file.Loaded;
        public string LoadWarning => file == null ? "" : file.LoadWarning;
        public string StoragePath => file == null ? "" : file.StoragePath;
        public CampaignProgressStore(string prefix = "Defend.Campaign.v1.", IEnumerable<string> knownLevelIds = null)
            : this(Path.Combine(Application.persistentDataPath, "CampaignProgress.json"), knownLevelIds,
                key => PlayerPrefs.GetInt(key, 0), prefix) { }
        /// <summary>路径与旧记录读取器可注入，测试不会接触玩家的正式存档。</summary>
        public CampaignProgressStore(string filePath, IEnumerable<string> knownLevelIds, Func<string, int> readLegacy,
            string prefix = "Defend.Campaign.v1.")
        {
            this.prefix = prefix;
            file = new CampaignProgressFile(filePath);
            if (knownLevelIds == null || readLegacy == null) return;
            foreach (string id in knownLevelIds)
                if (!string.IsNullOrWhiteSpace(id) && !file.IsCompleted(id) && readLegacy(prefix + id) == 1 && pending.Add(id)) Revision++;
            // 旧 PlayerPrefs 只读不删；迁移失败仍可在本次会话中使用，并交给现有重试入口。
            try { FlushPending(); }
            catch (Exception) { }
        }
        public CampaignProgressStore(Func<string, int> read, Action<string, int> write, Action save,
            string prefix = "Defend.Campaign.v1.")
        {
            this.read = read ?? throw new ArgumentNullException(nameof(read));
            this.write = write ?? throw new ArgumentNullException(nameof(write));
            this.save = save ?? throw new ArgumentNullException(nameof(save)); this.prefix = prefix;
        }
        public bool IsCompleted(string id) => !string.IsNullOrWhiteSpace(id) &&
            (pending.Contains(id) || (file != null ? file.IsCompleted(id) : read(prefix + id) == 1));
        public bool IsUnlocked(CampaignCatalogAsset catalog, int index)
        {
            if (catalog == null || index < 0 || index >= catalog.Levels.Count) return false;
            for (int i = 0; i < index; i++) if (!IsCompleted(catalog.Levels[i].Id)) return false;
            return true;
        }
        public bool QueueVictory(CampaignLevelAsset level, GameWorld world)
        {
            if (level == null || string.IsNullOrWhiteSpace(level.Id) || level.WaveCount <= 0 || world == null || world.IsSandbox || world.Battle == null ||
                world.Battle.State != BattleState.Victory || world.Battle.WaveNumber != level.WaveCount) return false;
            if (pending.Add(level.Id)) Revision++;
            return true;
        }
        public void FlushPending()
        {
            if (pending.Count == 0) return;
            try
            {
                if (file != null) file.Save(pending);
                else
                {
                    foreach (string id in pending) write(prefix + id, 1);
                    save();
                }
                pending.Clear(); LastSaveFailed = false; Revision++;
            }
            catch
            {
                if (!LastSaveFailed) { LastSaveFailed = true; Revision++; }
                throw;
            }
        }
        public bool RecordVictory(CampaignLevelAsset level, GameWorld world)
        {
            if (!QueueVictory(level, world)) return false;
            FlushPending(); return true;
        }
    }
}
