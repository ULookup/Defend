using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>进度文件只保存版本与已完成关卡；解锁关系由当前关卡目录推导。</summary>
    internal sealed class CampaignProgressFile
    {
        [Serializable]
        private sealed class Document
        {
            public int version;
            public string[] completedLevelIds;
        }
        private enum ReadState { Missing, Valid, Invalid, Unsupported }
        private sealed class ReadResult
        {
            public ReadState State;
            public Document Document;
            public string Warning = "";
        }
        private const int CurrentVersion = 1;
        private readonly HashSet<string> completed = new HashSet<string>(StringComparer.Ordinal);
        public string StoragePath { get; }
        public bool Loaded { get; private set; }
        public string LoadWarning { get; private set; } = "";
        private string BackupPath => StoragePath + ".bak";

        public CampaignProgressFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("存档路径不可为空。", nameof(path));
            StoragePath = Path.GetFullPath(path);
            LoadCurrent(out _);
        }

        public bool IsCompleted(string id) => completed.Contains(id);

        private static ReadResult Read(string path)
        {
            try
            {
                // File.Exists 会把权限异常当成“不存在”，直接读取才能保护不可访问的旧文件。
                string json = File.ReadAllText(path, Encoding.UTF8).Trim();
                if (!json.StartsWith("{", StringComparison.Ordinal) || !json.EndsWith("}", StringComparison.Ordinal))
                    throw new InvalidDataException("文件内容不完整。");
                var document = JsonUtility.FromJson<Document>(json);
                if (document == null) throw new InvalidDataException("文件内容不可读取。");
                if (document.version != CurrentVersion)
                    return new ReadResult { State = ReadState.Unsupported, Warning = "存档版本暂不支持，原文件已保留。" };
                if (document.completedLevelIds != null)
                    foreach (string id in document.completedLevelIds)
                        if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("关卡记录不完整。");
                return new ReadResult { State = ReadState.Valid, Document = document };
            }
            catch (FileNotFoundException) { return new ReadResult { State = ReadState.Missing }; }
            catch (DirectoryNotFoundException) { return new ReadResult { State = ReadState.Missing }; }
            catch (Exception)
            {
                return new ReadResult { State = ReadState.Invalid, Warning = "存档暂时无法读取，原文件已保留。" };
            }
        }

        private void Include(Document document)
        {
            if (document?.completedLevelIds == null) return;
            foreach (string id in document.completedLevelIds) completed.Add(id);
        }

        private bool LoadCurrent(out bool damagedPrimary)
        {
            damagedPrimary = false;
            var primary = Read(StoragePath);
            if (primary.State == ReadState.Valid)
            {
                Include(primary.Document); Loaded = true; LoadWarning = ""; return true;
            }
            // 较新版本不能用旧备份覆盖；让更新后的游戏继续读取它。
            if (primary.State == ReadState.Unsupported)
            {
                Loaded = false; LoadWarning = primary.Warning; return false;
            }
            var backup = Read(BackupPath);
            if (backup.State == ReadState.Valid)
            {
                Include(backup.Document); Loaded = true;
                damagedPrimary = primary.State == ReadState.Invalid;
                LoadWarning = "已从备用存档恢复通关进度。"; return true;
            }
            if (primary.State == ReadState.Missing && backup.State == ReadState.Missing)
            {
                Loaded = true; LoadWarning = ""; return true;
            }
            Loaded = false;
            LoadWarning = backup.State == ReadState.Unsupported ? backup.Warning :
                primary.State == ReadState.Invalid ? primary.Warning : backup.Warning;
            return false;
        }

        public void Save(IEnumerable<string> additions)
        {
            // 重试时重新读取，以恢复临时 I/O 故障，并保留文件中当前版本未识别的关卡 ID。
            if (!LoadCurrent(out bool damagedPrimary)) throw new IOException(LoadWarning);
            var previousBackup = Read(BackupPath);
            if (previousBackup.State == ReadState.Valid) Include(previousBackup.Document);
            var next = new HashSet<string>(completed, StringComparer.Ordinal);
            foreach (string id in additions) next.Add(id);
            var ids = new List<string>(next); ids.Sort(StringComparer.Ordinal);
            byte[] bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(
                new Document { version = CurrentVersion, completedLevelIds = ids.ToArray() }, true));
            string directory = Path.GetDirectoryName(StoragePath);
            Directory.CreateDirectory(directory);
            string temporary = StoragePath + ".tmp." + Guid.NewGuid().ToString("N");
            string temporaryBackup = BackupPath + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                if (damagedPrimary) PreserveDamagedFile(StoragePath);
                WriteTemporary(temporary, bytes);
                if (File.Exists(StoragePath)) File.Replace(temporary, StoragePath, null);
                else File.Move(temporary, StoragePath);

                // 通关集合只增加，两份文件都同步到最新集合后，才能报告保存完成。
                var backup = Read(BackupPath);
                if (backup.State == ReadState.Unsupported)
                {
                    LoadWarning = "备用存档版本暂不支持，原文件已保留。";
                    throw new IOException(LoadWarning);
                }
                if (backup.State == ReadState.Invalid) PreserveDamagedFile(BackupPath);
                WriteTemporary(temporaryBackup, bytes);
                if (File.Exists(BackupPath)) File.Replace(temporaryBackup, BackupPath, null);
                else File.Move(temporaryBackup, BackupPath);
                completed.UnionWith(next); Loaded = true; LoadWarning = "";
            }
            finally
            {
                // 仅清理由本次写入创建的临时文件；删除失败不掩盖原来的保存异常。
                DeleteTemporary(temporary); DeleteTemporary(temporaryBackup);
            }
        }

        private static void PreserveDamagedFile(string path)
        {
            // 修复前保留原文。若复制失败，原文件保持不动，通关记录仍留待重试。
            string copy = path + ".damaged." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "." + Guid.NewGuid().ToString("N");
            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var destination = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { source.CopyTo(destination); destination.Flush(true); }
        }

        private static void WriteTemporary(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }

        private static void DeleteTemporary(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
