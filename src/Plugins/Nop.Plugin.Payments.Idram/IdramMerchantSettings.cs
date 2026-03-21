// Decompiled with JetBrains decompiler
// Type: Nop.Plugin.Payments.Idram.IdramMerchantSettings
// Assembly: Nop.Plugin.Payments.Idram, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: ACB0BCDA-94BA-4FEB-8098-86C5829BC6E3
// Assembly location: C:\Workspace\MySnacks\Idram\Nop.Plugin.Payments.Idram.dll

using Nop.Core.Configuration;
using System;

namespace Nop.Plugin.Payments.Idram
{
  public class IdramMerchantSettings : ISettings
  {
    public bool UseSandbox { get; set; }

    public string PaymentUrl { get; set; }

    public string IdramId { get; set; }

    public string SecretKey { get; set; }

    public string MerchantEmail { get; set; }

    public Decimal AdditionalFee { get; set; }

    public bool AdditionalFeePercentage { get; set; }
  }
}
