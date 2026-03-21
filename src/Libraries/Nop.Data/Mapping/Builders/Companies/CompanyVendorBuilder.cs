using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Companies;
using Nop.Data.Extensions;
using Nop.Core.Domain.Vendors;

namespace Nop.Data.Mapping.Builders.Companies
{
    public partial class CompanyVendorBuilder : NopEntityBuilder<CompanyVendor>
    {
        public override void MapEntity(CreateTableExpressionBuilder table)
        {
            table
                .WithColumn(nameof(CompanyVendor.CompanyId)).AsInt32().ForeignKey<Company>()
                .WithColumn(nameof(CompanyVendor.VendorId)).AsInt32().ForeignKey<Vendor>();
        }
    }
}
