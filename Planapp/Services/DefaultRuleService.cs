using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Planapp.Models;

namespace Planapp.Services
{
    public class DefaultRuleService : IRuleService
    {
        private readonly string _rulesFilePath;
        private List<AppRule> _cachedRules = new();

        public DefaultRuleService()
        {
            var appDataPath = FileSystem.AppDataDirectory;
            _rulesFilePath = Path.Combine(appDataPath, "app_rules.json");
        }

        public async Task<List<AppRule>> GetRulesAsync()
        {
            try
            {
                if (File.Exists(_rulesFilePath))
                {
                    var json = await File.ReadAllTextAsync(_rulesFilePath);
                    _cachedRules = JsonSerializer.Deserialize<List<AppRule>>(json) ?? new List<AppRule>();
                }
                else
                {
                    _cachedRules = new List<AppRule>();
                }
            }
            catch
            {
                _cachedRules = new List<AppRule>();
            }
            return _cachedRules;
        }

        public async Task SaveRuleAsync(AppRule rule)
        {
            var rules = await GetRulesAsync();
            var existingRule = rules.FirstOrDefault(r => r.Id == rule.Id);

            if (existingRule != null)
            {
                var index = rules.IndexOf(existingRule);
                rules[index] = rule;
            }
            else
            {
                rules.Add(rule);
            }

            var json = JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_rulesFilePath, json);
            _cachedRules = rules;
        }

        public async Task DeleteRuleAsync(string ruleId)
        {
            var rules = await GetRulesAsync();
            rules.RemoveAll(r => r.Id == ruleId);

            var json = JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_rulesFilePath, json);
            _cachedRules = rules;
        }

        public virtual Task<List<Models.AppInfo>> GetAllAppsAsync()
        {
            return Task.FromResult(new List<Models.AppInfo>());
        }

        public virtual Task<long> GetCombinedUsageForAppsAsync(List<string> packageNames)
        {
            return Task.FromResult(0L);
        }
    }
}