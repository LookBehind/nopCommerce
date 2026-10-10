using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nop.Core.Domain.Catalog;

namespace Nop.Services.Catalog
{
    /// <summary>
    /// Captures a snapshot of the audit-relevant product fields and describes what changed
    /// ("Price: 2500 -> 2630"), so activity log entries show old and new values.
    /// </summary>
    public static class ProductChangeTracker
    {
        private const int MAX_VALUE_LENGTH = 200;

        private static readonly (string Label, Func<Product, object> Get)[] _tracked =
        {
            ("Name", p => p.Name),
            ("ShortDescription", p => p.ShortDescription),
            ("Sku", p => p.Sku),
            ("Price", p => p.Price),
            ("OldPrice", p => p.OldPrice),
            ("ProductCost", p => p.ProductCost),
            ("CallForPrice", p => p.CallForPrice),
            ("CustomerEntersPrice", p => p.CustomerEntersPrice),
            ("Published", p => p.Published),
            ("VisibleIndividually", p => p.VisibleIndividually),
            ("VendorId", p => p.VendorId),
            ("StockQuantity", p => p.StockQuantity),
            ("OrderMinimumQuantity", p => p.OrderMinimumQuantity),
            ("OrderMaximumQuantity", p => p.OrderMaximumQuantity),
            ("AllowedQuantities", p => p.AllowedQuantities),
            ("DisableBuyButton", p => p.DisableBuyButton),
            ("AvailableStartDateTimeUtc", p => p.AvailableStartDateTimeUtc),
            ("AvailableEndDateTimeUtc", p => p.AvailableEndDateTimeUtc)
        };

        /// <summary>
        /// Takes a snapshot of the tracked fields. Call before the entity is modified.
        /// </summary>
        public static IReadOnlyDictionary<string, string> Snapshot(Product product)
        {
            return _tracked.ToDictionary(t => t.Label, t => Format(t.Get(product)));
        }

        /// <summary>
        /// Describes the differences between a snapshot and the current state of the product.
        /// </summary>
        /// <returns>E.g. "Price: 2500 -> 2630; Published: True -> False", or an empty string if nothing tracked changed</returns>
        public static string Describe(IReadOnlyDictionary<string, string> before, Product after)
        {
            var changes = _tracked
                .Select(t => (t.Label, Old: before[t.Label], New: Format(t.Get(after))))
                .Where(c => c.Old != c.New)
                .Select(c => $"{c.Label}: {Truncate(c.Old)} -> {Truncate(c.New)}");

            return string.Join("; ", changes);
        }

        private static string Format(object value)
        {
            return value switch
            {
                null => "(empty)",
                decimal d => d.ToString("0.####", CultureInfo.InvariantCulture),
                DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                string s when string.IsNullOrEmpty(s) => "(empty)",
                _ => Convert.ToString(value, CultureInfo.InvariantCulture)
            };
        }

        private static string Truncate(string value)
        {
            return value.Length <= MAX_VALUE_LENGTH ? value : value[..MAX_VALUE_LENGTH] + "...";
        }
    }
}
