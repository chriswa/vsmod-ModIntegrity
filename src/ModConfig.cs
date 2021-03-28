using Vintagestory.API.Common;

namespace ModIntegrity {
  public class ModConfig {

    public int ClientReportGraceSeconds = 15;
    public string ExtraDisconnectMessage = "Please contact the server owner with any problems or to request new mods.";
    public ModReport[] AllowedClientOnlyMods = new ModReport[] { };

    // static helper methods
    public static ModConfig Load(ICoreAPI api) {
      var config = api.LoadModConfig<ModConfig>("ModIntegrity.json");
      if (config == null) {
        config = new ModConfig();
        api.StoreModConfig(config, "ModIntegrity.json");
      }
      return config;
    }
    public static void Save(ICoreAPI api, ModConfig config) {
      api.StoreModConfig(config, "ModIntegrity.json");
    }
  }
}
