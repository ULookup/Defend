using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>三十波正式关卡的制作数据。制作器将确定的逐入口、逐兵种、逐时刻计划保存进战斗资产。</summary>
    public static class FormalLevelDefinition
    {
        public const int WaveCount = 30;
        public const int StartingCredits = 1500;
        public const int MaximumLivingEnemies = 200;
        private static readonly int[,] EnemyCounts = {
            {12,0,0,0,0},{16,0,0,0,0},{12,8,0,0,0},{10,14,0,0,0},{16,12,0,0,0},
            {20,10,2,0,0},{12,20,4,0,0},{18,14,2,6,0},{12,20,4,8,0},{12,18,6,6,6},
            {24,16,4,6,4},{16,24,6,8,6},{18,24,8,10,6},{14,30,6,12,10},{20,28,10,10,10},
            {14,36,8,14,12},{20,32,12,14,12},{16,40,10,16,14},{30,34,10,14,14},{20,40,14,18,16},
            {16,50,12,20,18},{24,44,18,20,18},{20,52,16,24,20},{28,48,20,22,22},{20,58,20,26,24},
            {26,60,22,26,22},{20,64,24,28,28},{28,62,26,28,28},{24,72,24,30,30},{30,70,32,34,34}
        };
        private static readonly int[,] EntranceCounts = {
            {12,0,0,0},{16,0,0,0},{20,0,0,0},{24,0,0,0},{16,12,0,0},
            {16,16,0,0},{18,18,0,0},{16,24,0,0},{26,18,0,0},{24,24,0,0},
            {20,20,14,0},{20,20,20,0},{24,24,18,0},{24,26,22,0},{26,26,26,0},
            {28,28,28,0},{30,28,32,0},{30,34,32,0},{30,30,26,16},{28,28,26,26},
            {30,30,28,28},{32,32,30,30},{34,34,32,32},{36,36,34,34},{38,38,36,36},
            {40,40,38,38},{42,42,40,40},{44,44,42,42},{45,45,45,45},{50,50,50,50}
        };
        private static readonly int[] Health = {60,60,62,64,66,68,72,76,80,84,84,88,92,96,102,102,108,114,114,120,126,132,138,144,150,150,156,162,168,180};
        private static readonly int[] Durations = {18,20,22,24,28,30,32,34,36,40,38,42,44,46,50,48,52,54,52,58,56,60,62,64,68,64,68,70,72,80};
        private static readonly int[] Rewards = {100,100,100,100,160,80,80,80,80,140,100,60,60,60,120,50,50,50,100,100,30,30,30,30,80,0,0,0,0,0};
        private static readonly string[] Briefings = {
            "西侧下方入口来袭，先建立稳定供能与交叉火力。", "巡猎者增援，检查火力覆盖是否存在空隙。",
            "拭净者首次出现，预告西侧上方入口将在第 5 波开放。", "快速敌人占比提高，为第二条防线预留资金。",
            "西侧上方入口开放，新入口由巡猎者先行，防守双线。", "镇载者首次出现，先处理推进缓慢但耐久较高的威胁。",
            "两线快速敌人增加，避免单一方向消耗全部储能。", "拆迁者首次出现，保护连接前沿的中继器。",
            "北侧入口将在第 11 波开放，准备侧翼供电与火力。", "收束者首次出现，保护反应堆并准备第三条防线。",
            "北侧入口开放，由巡猎者先行，原有双线继续施压。", "三线混编进攻，检查备用供能路径。",
            "重型敌人增加，集中火力时仍需照顾快速敌群。", "拆迁者与收束者压迫能源网络，分散关键节点。",
            "三线阶段检验，重型掩护与快速穿插连续到来。", "减少固定补助后需要兼顾维修与扩建。",
            "南侧入口将在第 19 波开放，准备第四个进攻方向。", "三线持续进攻，保留应对南侧开放的储备。",
            "南侧入口开放，由巡猎者先行，四个方向开始同时承压。", "四线混编检验，避免把能源全部押在单个反应堆。",
            "快速敌群消耗持续火力，下一批仍有攻坚单位。", "镇载者增援，重火力需要稳定供能。",
            "拆迁者压迫多处中继，守住能源网络的连接。", "四线交替攻坚，保持各方向防线完整。",
            "最后一次固定补助，之后仅靠击杀收入维持防线。", "持续消耗战开始，精准分配维修与重建资金。",
            "四线拆迁与收束单位增加，避免连续失去供电节点。", "重型单位先行，快速敌人在各批后段跟进。",
            "决战前最后攻势，检验供能余量和全部交叉火力。", "最终决战：四入口各 50 名敌人，三批连续混编进攻。"
        };

        public static SpawnPoint[] CreateEntrances() => new[] {
            new SpawnPoint(0,10,"西侧下方",1,2,0,9,3,3),
            new SpawnPoint(0,30,"西侧上方",5,2,0,29,3,3),
            new SpawnPoint(18,39,"北侧",11,2,17,37,3,3),
            new SpawnPoint(18,0,"南侧",19,2,17,0,3,3)
        };

        public static int KillReward(EnemyKind kind)
        {
            switch (kind) {
                case EnemyKind.Normal: return 2; case EnemyKind.Fast: return 1; case EnemyKind.Heavy: return 6;
                case EnemyKind.Demolisher: return 3; case EnemyKind.Converger: return 4;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static WaveDefinition[] CreateWaves(int ticksPerSecond = 20)
        {
            if (ticksPerSecond < 1) throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
            var result = new WaveDefinition[WaveCount];
            for (int wave = 0; wave < WaveCount; wave++)
            {
                var allocations = AllocateEnemies(wave);
                var groups = new List<WaveGroupDefinition>();
                int duration = checked(Durations[wave] * ticksPerSecond);
                for (int entrance = 0; entrance < 4; entrance++)
                    BuildEntranceTimeline(allocations, entrance, duration, groups);
                groups.Sort((a,b) => a.StartTick != b.StartTick ? a.StartTick.CompareTo(b.StartTick) : a.EntranceIndex.CompareTo(b.EntranceIndex));
                result[wave] = new WaveDefinition(groups.Count, Health[wave], 1, Rewards[wave], briefing: Briefings[wave],
                    groups: groups.ToArray(), independentGroups: true, plannedSpawnDurationTicks: duration);
            }
            return result;
        }

        private static int[,] AllocateEnemies(int wave)
        {
            var result = new int[4,5]; var remaining = new int[4]; var kinds = new int[5];
            for (int i = 0; i < 4; i++) remaining[i] = EntranceCounts[wave,i];
            for (int k = 0; k < 5; k++) kinds[k] = EnemyCounts[wave,k];
            int firstEntrance = wave == 4 ? 1 : wave == 10 ? 2 : wave == 18 ? 3 : -1;
            if (firstEntrance >= 0)
            { result[firstEntrance,0] = remaining[firstEntrance]; kinds[0] -= remaining[firstEntrance]; remaining[firstEntrance] = 0; }
            // 重型偏西侧主通道，拆迁者偏北侧中继，收束者偏南北侧翼；配额严格守恒。
            int[,] preference = { {3,3,1,1}, {1,3,3,2}, {1,1,4,4}, {2,3,2,3}, {1,1,1,1} };
            int[] order = {2,3,4,1,0};
            for (int row = 0; row < order.Length; row++)
            {
                int kind = order[row];
                for (int unit = 0; unit < kinds[kind]; unit++)
                {
                    int best = -1; double score = -1;
                    for (int entrance = 0; entrance < 4; entrance++)
                    {
                        if (remaining[entrance] == 0) continue;
                        double candidate = (double)remaining[entrance] * preference[row,entrance] / (result[entrance,kind] + 1);
                        if (candidate > score) { score = candidate; best = entrance; }
                    }
                    if (best < 0) throw new InvalidOperationException("正式关卡兵种数量超过入口配额。");
                    result[best,kind]++; remaining[best]--;
                }
            }
            for (int i = 0; i < 4; i++) if (remaining[i] != 0) throw new InvalidOperationException("正式关卡入口配额未分配完整。");
            return result;
        }

        private static void BuildEntranceTimeline(int[,] allocations, int entrance, int duration, List<WaveGroupDefinition> groups)
        {
            var phases = new int[3,5];
            for (int kind = 0; kind < 5; kind++)
            {
                int count = allocations[entrance,kind];
                // 重型前置，快速后置；每批仍包含其他已有兵种，避免整波按敌种排成长队。
                phases[0,kind] = kind == 2 ? (count + 1) / 2 : kind == 1 ? count / 4 : (count + 2) / 3;
                phases[1,kind] = kind == 2 ? (count - phases[0,kind] + 1) / 2 : kind == 1 ? (count + 1) / 3 : (count + 1) / 3;
                phases[2,kind] = count - phases[0,kind] - phases[1,kind];
            }
            for (int phase = 0; phase < 3; phase++)
            {
                var sequence = new List<EnemyKind>();
                int[] order = {2,3,0,4,1};
                // 每轮穿插各类敌人；快速单位集中在后半程，重型从批次开头进入。
                while (true)
                {
                    bool added = false;
                    foreach (int kind in order)
                        if (phases[phase,kind] > 0) { sequence.Add((EnemyKind)kind); phases[phase,kind]--; added = true; }
                    if (!added) break;
                }
                if (sequence.Count == 0) continue;
                // 将本批第一个重型提前、快速队尾后置，同时保留中间的混编。
                int fastIndex = sequence.FindLastIndex(value => value == EnemyKind.Fast);
                if (fastIndex >= 0) { sequence.RemoveAt(fastIndex); sequence.Add(EnemyKind.Fast); }
                int start = phase * duration / 3;
                int end = phase == 2 ? duration : (phase + 1) * duration / 3 - 1;
                for (int unit = 0; unit < sequence.Count; unit++)
                {
                    int tick = sequence.Count == 1 ? start : start + (end - start) * unit / (sequence.Count - 1);
                    groups.Add(new WaveGroupDefinition(sequence[unit], 1, entrance, tick, 1));
                }
            }
        }
    }
}
