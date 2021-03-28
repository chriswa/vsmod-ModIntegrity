using ProtoBuf;
using Vintagestory.API.Common;

namespace ModIntegrity {
  [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
  public class ModReport {
    public string Id;
    public string Name;
    public string Version;
    public string FileName;
    public string SourceType;
    public string Fingerprint;

    public static ModReport CreateForMod(Mod mod) {
      return new ModReport() {
        Id = mod.Info.ModID,
        Name = mod.Info.Name,
        Version = mod.Info.Version,
        FileName = mod.FileName,
        SourceType = mod.SourceType.ToString(),
        Fingerprint = Md5Tools.md5Mod(mod)
      };
    }
  }
}