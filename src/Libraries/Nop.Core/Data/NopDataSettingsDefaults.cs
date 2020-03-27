using System;
namespace Nop.Core.Data
{
    /// <summary>
    /// Represents default values related to data settings
    /// </summary>
    public static partial class NopDataSettingsDefaults
    {
        /// <summary>
        /// Gets a path to the file that was used in old nopCommerce versions to contain data settings
        /// </summary>
        public static string ObsoleteFilePath => Environment.GetEnvironmentVariable("NOP_OBSOLETTESETTINGS_PATH") ?? "~/App_Data/Settings.txt";

        /// <summary>
        /// Gets a path to the file that contains data settings
        /// </summary>
        public static string FilePath => Environment.GetEnvironmentVariable("NOP_DATASETTINGS_PATH") ?? "~/App_Data/dataSettings.json";
    }
}