using System;
namespace Defend.Simulation
{
    /// <summary>敌人类型配置独立于实例。伤害、体积与奖励直接由模拟使用，界面读取同一份资料。</summary>
    public sealed class EnemyDefinition
    {
        public EnemyKind Kind { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string AttackDescription => "近战单体";
        public int BaseHealth { get; }
        public double Radius { get; }
        public double Speed { get; }
        public double BodyHeight { get; }
        public int AttackDamage { get; }
        public int AttackIntervalTicks { get; }
        public double AttackRange { get; }
        public int KillReward { get; }
        public double RelayPreference { get; }
        public double ReactorPreference { get; }
        public double RiskWeight { get; }
        public EnemyDefinition(EnemyKind kind, string displayName, string description, int baseHealth,
            double radius, double speed, int attackDamage, int attackIntervalTicks, double attackRange,
            int killReward, double relayPreference = 1, double reactorPreference = 1, double riskWeight = 8, double bodyHeight = 0)
        {
            if (!Enum.IsDefined(typeof(EnemyKind), kind) || baseHealth <= 0 || attackDamage <= 0 || attackIntervalTicks <= 0 || killReward < 0)
                throw new ArgumentOutOfRangeException(nameof(baseHealth));
            if (!Finite(radius) || radius <= 0 || radius > 0.49 || !Finite(speed) || speed <= 0 ||
                !Finite(attackRange) || attackRange < radius || !Finite(relayPreference) || relayPreference <= 0 ||
                !Finite(reactorPreference) || reactorPreference <= 0 || !Finite(riskWeight) || riskWeight < 0)
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (!Finite(bodyHeight) || bodyHeight < 0) throw new ArgumentOutOfRangeException(nameof(bodyHeight));
            BodyHeight = bodyHeight > 0 ? bodyHeight : (kind == EnemyKind.Fast ? .55 : kind == EnemyKind.Heavy ? 1.3 : .9);
            Kind = kind; DisplayName = displayName; Description = description; BaseHealth = baseHealth;
            Radius = radius; Speed = speed; AttackDamage = attackDamage; AttackIntervalTicks = attackIntervalTicks;
            AttackRange = attackRange; KillReward = killReward; RelayPreference = relayPreference;
            ReactorPreference = reactorPreference; RiskWeight = riskWeight;
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static EnemyDefinition Default(EnemyKind kind, double speed = 1.8, int damage = 8, int interval = 20)
        {
            switch (kind)
            {
                case EnemyKind.Fast: return new EnemyDefinition(kind, "拭净者", "曾经穿行旧城的清扫虫群，如今快速成群接近薄弱工事，消耗防线火力与储能。", 40, .22, speed * 1.8, damage, interval, .56, 2);
                case EnemyKind.Heavy: return new EnemyDefinition(kind, "镇载者", "沉重的碾压装置始终向核心推进，比较火力威胁与拆除代价，清除必要障碍。", 300, .46, speed * .55, damage * 3, interval, .64, 18, riskWeight: 5);
                case EnemyKind.Demolisher: return new EnemyDefinition(kind, "拆迁者", "原为切断纠缠线路的装置，如今倾向拆除中继器，也会选择更易破坏的高收益工事。", 95, .36, speed * .9, damage * 2, interval, .6, 9, relayPreference: 5);
                case EnemyKind.Converger: return new EnemyDefinition(kind, "收束者", "拖着履带与回收斗寻找能源，倾向破坏反应堆，切断真实支撑防线的能源源头。", 115, .38, speed * .8, damage * 2, interval, .6, 11, reactorPreference: 5);
                default: return new EnemyDefinition(kind, "巡猎者", "旧城的清障装置仍执行数百年前的命令；比较全图工事的价值与进攻代价，也会压制正在造成伤害的炮塔。", 60, .32, speed, damage, interval, .58, 5);
            }
        }
    }
}
