using System.Linq;
using System.Text.Json;
using FluentMigrator;
using Nop.Core.Domain.Configuration;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Company.Company.Migrations
{
    [NopMigration("2026/03/19 12:00:00:0000000", "Company.ConvertScheduleDateCsvToJson")]
    [SkipMigrationOnInstall]
    public class ConvertScheduleDateCsvToJsonMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public ConvertScheduleDateCsvToJsonMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            var settings = _dataProvider.GetTable<Setting>()
                .Where(s => s.Name == "ordersettings.scheduledate"
                         && s.Value != null
                         && s.Value != "")
                .ToList();

            foreach (var setting in settings)
            {
                var raw = setting.Value.Trim();

                // Already JSON — skip
                if (raw.StartsWith("["))
                    continue;

                // Parse CSV triplets: "HH:MM:SS-HH:MM:SS-HH:MM:SS,..."
                var slots = raw.Split(',')
                    .Select((csv, idx) =>
                    {
                        var parts = csv.Trim().Split('-');
                        if (parts.Length < 3)
                            return null;

                        return new
                        {
                            openTime = parts[0].Length > 5 ? parts[0].Substring(0, 5) : parts[0],
                            cutoffTime = parts[1].Length > 5 ? parts[1].Substring(0, 5) : parts[1],
                            deliveryTime = parts[2].Length > 5 ? parts[2].Substring(0, 5) : parts[2],
                            isEnabled = true,
                            sortOrder = idx
                        };
                    })
                    .Where(s => s != null)
                    .ToList();

                if (slots.Any())
                {
                    setting.Value = JsonSerializer.Serialize(slots);
                    _dataProvider.UpdateEntityAsync(setting).GetAwaiter().GetResult();
                }
            }
        }

        public override void Down()
        {
            // No rollback — CSV format is lossy (no isEnabled/sortOrder)
        }
    }
}
