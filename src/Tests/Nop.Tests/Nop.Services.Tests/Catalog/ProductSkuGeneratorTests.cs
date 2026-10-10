using FluentAssertions;
using Nop.Services.Catalog;
using NUnit.Framework;

namespace Nop.Tests.Nop.Services.Tests.Catalog
{
    [TestFixture]
    public class ProductSkuGeneratorTests
    {
        [Test]
        public void BuildSkuShouldLowercaseAndStripWhitespace()
        {
            ProductSkuGenerator.BuildSku("Srtants", 11627).Should().Be("srtants-11627");
            ProductSkuGenerator.BuildSku("33 Pizzas", 1133).Should().Be("33pizzas-1133");
            ProductSkuGenerator.BuildSku("The Toast ST Cascade", 41499).Should().Be("thetoaststcascade-41499");
        }

        [Test]
        public void BuildSkuShouldKeepNonWhitespaceCharactersVerbatim()
        {
            ProductSkuGenerator.BuildSku("Bar B.Q.", 1287).Should().Be("barb.q.-1287");
            ProductSkuGenerator.BuildSku("Wok & Roll", 2263).Should().Be("wok&roll-2263");
        }

        [Test]
        public void BuildSkuShouldReturnNullForMissingVendorName()
        {
            ProductSkuGenerator.BuildSku(null, 5).Should().BeNull();
            ProductSkuGenerator.BuildSku("", 5).Should().BeNull();
            ProductSkuGenerator.BuildSku("   ", 5).Should().BeNull();
        }

        [Test]
        public void SanitizeVendorNameShouldMatchBuildSkuPrefix()
        {
            ProductSkuGenerator.SanitizeVendorName("9 AM").Should().Be("9am");
            ProductSkuGenerator.SanitizeVendorName("  Pekin\t").Should().Be("pekin");
        }
    }
}
