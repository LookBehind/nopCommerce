// Decompiled with JetBrains decompiler
// Type: Nop.Plugin.Payments.Idram.Models.ConfigurationModel
// Assembly: Nop.Plugin.Payments.Idram, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: ACB0BCDA-94BA-4FEB-8098-86C5829BC6E3
// Assembly location: C:\Workspace\MySnacks\Idram\Nop.Plugin.Payments.Idram.dll

using Nop.Web.Framework.Mvc.ModelBinding;
using System;

namespace Nop.Plugin.Payments.Idram.Models
{
  public class ConfigurationModel
  {
    public int ActiveStoreScopeConfiguration { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Idram.Fields.UseSandbox")]
    public bool UseSandbox { get; set; }

    public bool UseSandbox_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Idram.Fields.PaymentUrl")]
    public string PaymentUrl { get; set; }

    public bool PaymentUrl_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Idram.Fields.IdramId")]
    public string IdramId { get; set; }

    public bool IdramId_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Idram.Fields.SecretKey")]
    public string SecretKey { get; set; }

    public bool SecretKey_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Idram.Fields.MerchantEmail")]
    public string MerchantEmail { get; set; }

    public bool MerchantEmail_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Idram.Fields.AdditionalFee")]
    public Decimal AdditionalFee { get; set; }

    public bool AdditionalFee_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Idram.Fields.AdditionalFeePercentage")]
    public bool AdditionalFeePercentage { get; set; }

    public bool AdditionalFeePercentage_OverrideForStore { get; set; }
  }
}
