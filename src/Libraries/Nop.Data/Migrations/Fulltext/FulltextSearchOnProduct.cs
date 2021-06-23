using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Tasks;
using Nop.Data.Mapping;

namespace Nop.Data.Migrations.FulltextSearchOnProduct
{
    [NopMigration("2020-06-23 11:44:00:6453255", "4.40.0", UpdateMigrationType.Data, TransactionBehavior.None)]
    [SkipMigrationOnInstall]
    public class FulltextSearchOnProductMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public FulltextSearchOnProductMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        /// <summary>
        /// Collect the UP migration expressions
        /// </summary>
        public override void Up()
        {
            var productTableName = NameCompatibilityManager.GetTableName(typeof(Product));
            var productNameColumnName = NameCompatibilityManager.GetColumnName(typeof(Product), "Name");
            IfDatabase("SqlServer")
                .Execute.Sql("CREATE FULLTEXT CATALOG ft AS DEFAULT;" + 
                    $"CREATE FULLTEXT INDEX ON [dbo].[{productTableName}]" + 
                    $"([{productNameColumnName}])" +
                    "KEY INDEX PK_Product;"
                );
        }

        public override void Down()
        {
            //add the downgrade logic if necessary 
        }
    }
}
