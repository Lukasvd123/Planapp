using System;
using System.Threading.Tasks;
using com.usagemeter.androidapp.Models;

namespace com.usagemeter.androidapp.Services
{
    public interface IRuleBlockService
    {
        event EventHandler<RuleBlockEventArgs>? RuleTriggered;
        event EventHandler? RuleAcknowledged;

        Task TriggerRuleBlock(AppRule rule);
        Task AcknowledgeBlock();
        Task OpenTargetApp(string packageName);

        bool IsBlocking { get; }
        AppRule? CurrentBlockedRule { get; }
    }

    public class RuleBlockEventArgs : EventArgs
    {
        public AppRule Rule { get; set; } = null!;
        public DateTime TriggeredAt { get; set; } = DateTime.Now;
    }
}