using System.Collections.Generic;
using System.Threading.Tasks;
using Planapp.Models;

namespace Planapp.Services
{
    public interface IRuleService
    {
        Task<List<AppRule>> GetRulesAsync();
        Task SaveRuleAsync(AppRule rule);
        Task DeleteRuleAsync(string ruleId);
        Task<List<Models.AppInfo>> GetAllAppsAsync();
        Task<long> GetCombinedUsageForAppsAsync(List<string> packageNames);
    }
}