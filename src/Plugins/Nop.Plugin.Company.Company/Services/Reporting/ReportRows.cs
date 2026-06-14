using System;

namespace Nop.Plugin.Company.Company.Services.Reporting
{
    // Row POCOs for IReportService widget queries (column names must match the SQL aliases).
    // Property declaration order = ReportResult column order.

    internal class VendorTotalRow
    {
        public string Vendor { get; set; }
        public int Quantity { get; set; }
        public decimal Total { get; set; }
    }

    internal class VendorDayRow
    {
        public string Vendor { get; set; }
        public DateTime Day { get; set; }
        public decimal Total { get; set; }
    }

    internal class AvgChequeRow
    {
        public DateTime Day { get; set; }
        public decimal AvgCheque { get; set; }
    }

    internal class ActivePersonsRow
    {
        public int OrderCount { get; set; }
        public int UniqueCustomers { get; set; }
    }

    internal class OrderListRow
    {
        public string Employee { get; set; }
        public DateTime OrderDate { get; set; }
        public string DeliveryHour { get; set; }
        public string Email { get; set; }
        public decimal OrderTotal { get; set; }
    }
}
