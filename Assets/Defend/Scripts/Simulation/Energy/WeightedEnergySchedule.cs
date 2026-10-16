using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>
    /// 按最终有效到货量计费的加权额度轮询。成功服务后推进轮次，未用完的额度有界保留。
    /// 调用方先遍历仍有额度的可行目标，全部不可服务后才开启下一轮。
    /// </summary>
    internal sealed partial class WeightedEnergySchedule
    {
        private sealed class Account
        {
            public long Credit;
            public int Weight;
            public BuildingState Consumer;
        }
        private readonly Dictionary<long, Account> accounts = new Dictionary<long, Account>();
        private readonly long quantum;
        private bool madeProgress;
        public int Cursor { get; set; }

        public WeightedEnergySchedule(long quantum) { this.quantum = quantum; }

        public long Credit(BuildingState consumer)
        {
            if (!accounts.TryGetValue(consumer.Id, out Account account))
            {
                account = new Account { Weight = consumer.PriorityWeight, Credit = Allowance(consumer), Consumer = consumer };
                accounts.Add(consumer.Id, account);
            }
            // 修改权重立即限制未来余额，不追溯已交付的能量，也不保留旧高权重的巨额信用。
            if (account.Weight != consumer.PriorityWeight)
            {
                account.Credit = Math.Min(account.Credit, Allowance(consumer));
                account.Weight = consumer.PriorityWeight;
            }
            return account.Credit;
        }

        public void Spend(BuildingState consumer, long effectiveAmount)
        {
            Credit(consumer);
            accounts[consumer.Id].Credit -= effectiveAmount;
            if (effectiveAmount > 0) madeProgress = true;
        }

        public void Refund(BuildingState consumer, long effectiveAmount)
        {
            Credit(consumer);
            Account account = accounts[consumer.Id];
            account.Credit = Math.Min(MaximumCredit(consumer), checked(account.Credit + effectiveAmount));
        }

        public void BeginNextRound()
        {
            // 出口只为实际参与过运输的目标维护份额，不为整个地图的终端建立空账户。
            foreach (Account account in accounts.Values)
            {
                BuildingState consumer = account.Consumer;
                if (!consumer.IsAlive || consumer.IsSold) { account.Credit = 0; continue; }
                // 原始输出的取整尾数可能暂时不足以交付最后一份有效额度。
                // 下一轮保留这份额度并统一增加各自权重，不能只给提前耗尽余额的一端续发整轮。
                // 最多保留两轮；没有实际发送时不累加，避免空仓或断路期间囤积未来份额。
                if (madeProgress) account.Credit = Math.Min(MaximumCredit(consumer), checked(account.Credit + Allowance(consumer)));
                else if (account.Credit <= 0) account.Credit = Allowance(consumer);
            }
            madeProgress = false;
        }

        public void Forget(long buildingId) { accounts.Remove(buildingId); }
        private long Allowance(BuildingState consumer) { return checked(quantum * Math.Max(1, consumer.PriorityWeight)); }
        private long MaximumCredit(BuildingState consumer) { return checked(Allowance(consumer) * 2); }
    }
}
