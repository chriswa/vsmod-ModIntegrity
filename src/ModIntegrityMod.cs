using ProtoBuf;
using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using System.Linq;
using System.Collections.Generic;
using Vintagestory.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

[assembly: ModInfo("ModIntegrity")]

namespace ModIntegrity {
  [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
  public class NetworkApiModIntegrityPacket {
    public List<ModReport> ModReports = new List<ModReport>();
  }
  public class ModIntegrityMod : ModSystem {
    public override double ExecuteOrder() {
      return Double.NegativeInfinity;
    }
    Harmony harmony;
    public override void StartPre(ICoreAPI api) {
      api.Logger.Debug("ModIntegrity: StartPre");
      api.Network.RegisterChannel("modintegrity").RegisterMessageType(typeof(NetworkApiModIntegrityPacket));

      if (api.Side == EnumAppSide.Client) {
        // Patch_ClientMain_Connect.capi = api as ICoreClientAPI;
        // Patch_ClientMain_Connect.clientNetworkChannel = networkChannel as Vintagestory.Client.NoObf.NetworkChannel;
        StartPreClientSide(api as ICoreClientAPI);
      }
      else {
        StartPreServerSide(api as ICoreServerAPI);
        // Patch_ServerMain_HandlePlayerIdentification.NetworkChannel = (api as ICoreServerAPI).Network.GetChannel("modintegrity");
      }

      harmony = new Harmony("goxmeor.modintegrity");
      harmony.PatchAll();
    }
    public void StartPreClientSide(ICoreClientAPI capi) {
      capi.Logger.Debug($"ModIntegrity: Client starting up...");
      var packet = new NetworkApiModIntegrityPacket();
      foreach (var mod in capi.ModLoader.Mods) {
        var modReport = ModReport.CreateForMod(mod);
        packet.ModReports.Add(modReport);
        capi.Logger.Debug($"ModIntegrity: ModReport: \"{modReport.Name}\" ({modReport.Id}), a {modReport.SourceType} at {mod.FileName} with fingerprint {modReport.Fingerprint}");
      }
      capi.Event.PlayerJoin += (IClientPlayer byPlayer) => {
        capi.Network.GetChannel("modintegrity").SendPacket(packet);
        capi.Logger.Debug($"ModIntegrity: Client sent modReports packet!");
      };
    }
    private AllowList allowList = new AllowList();
    private ModConfig config;
    private Dictionary<string, DateTime> playerUidsWhoHaventReported = new Dictionary<string, DateTime>();
    private Dictionary<string, List<ModReport>> recentUnrecognizedModReportsByPlayerUid = new Dictionary<string, List<ModReport>>();
    private double TEMPlongestGraceRequired = 0;
    public void StartPreServerSide(ICoreServerAPI sapi) {
      sapi.Logger.Debug($"ModIntegrity: Server starting up...");
      config = ModConfig.Load(sapi);
      // auto-allow universal mods (server-only are added too, but that doesn't matter)
      foreach (var serverMod in sapi.ModLoader.Mods) {
        allowList.AddModReport(ModReport.CreateForMod(serverMod));
      }
      // allow everything in the json config
      foreach (var configAllowedModReport in config.AllowedClientOnlyMods) {
        allowList.AddModReport(configAllowedModReport);
      }
      // when a player joins, set a timer to kick if they haven't submitted a packet yet
      sapi.Event.PlayerNowPlaying += ((IServerPlayer player) => {
        sapi.Logger.Debug("ModIntegrity: Start player nowPlaying timeout");
        playerUidsWhoHaventReported.Add(player.PlayerUID, DateTime.Now);
        recentUnrecognizedModReportsByPlayerUid.Remove(player.PlayerUID);
        sapi.World.RegisterCallback((float deltaTime) => {
          if (playerUidsWhoHaventReported.ContainsKey(player.PlayerUID)) {
            playerUidsWhoHaventReported.Remove(player.PlayerUID);
            sapi.Logger.Event($"ModIntegrity: kicking {player.PlayerName} ({player.PlayerUID}) for taking too long to report mods. To change the timeout, change ClientReportGraceSeconds in ModIntegrity.json");
            DisconnectPlayerWithFriendlyMessage(player, "ModIntegrity: Timed out waiting for your client's report. Please try again?");
          }
        }, 1000 * config.ClientReportGraceSeconds);
      });
      // clean up when players leave
      sapi.Event.PlayerLeave += ((IServerPlayer player) => {
        playerUidsWhoHaventReported.Remove(player.PlayerUID);
      });
      // when we receive a packet from a client, check their reported mods! (and disconnect them if necessary)
      sapi.Network.GetChannel("modintegrity").SetMessageHandler<NetworkApiModIntegrityPacket>((IServerPlayer player, NetworkApiModIntegrityPacket packet) => {
        if (playerUidsWhoHaventReported.TryGetValue(player.PlayerUID, out var startTime)) {
          var totalMs = (DateTime.Now - startTime).TotalMilliseconds;
          sapi.Logger.Event($"ModIntegrity got a packet from {player.PlayerName} ({player.PlayerUID}) after {totalMs} ms");
          TEMPlongestGraceRequired = Math.Max(TEMPlongestGraceRequired, totalMs);
        }
        else {
          sapi.Logger.Error($"ModIntegrity: Internal Error! Packet received from {player.PlayerName} ({player.PlayerUID}), but no time was recorded in playerUidsWhoHaventReported?!");
          return;
        }
        playerUidsWhoHaventReported.Remove(player.PlayerUID);
        var unrecognizedModReportList = new List<ModReport>();
        var modIssuesForClient = new List<string>();
        foreach (var clientModReport in packet.ModReports) {
          var problem = allowList.GetClientModReportProblem(clientModReport);
          if (problem != AllowList.ProblemKind.None) {
            unrecognizedModReportList.Add(clientModReport);
            switch (problem) {
              case AllowList.ProblemKind.UnrecognizedModId:
                modIssuesForClient.Add(Lang.Get("Unrecognized or banned mod \"{0}\" — please disable this mod using the in-game Mod Manager.", clientModReport.Name));
                break;
              case AllowList.ProblemKind.UnrecognizedVersion:
                modIssuesForClient.Add(Lang.Get("Unrecognized or banned version \"{0}\" for mod \"{1}\" — please update to a known good version, such as: {2}",
                  clientModReport.Version, clientModReport.Name, string.Join(", ", allowList.GetAllowedVersionsForMod(clientModReport.Id))
                ));
                break;
              case AllowList.ProblemKind.UnrecognizedSourceType:
                modIssuesForClient.Add(Lang.Get("Unrecognized or banned source type \"{0}\" for mod \"{1}\" — please update this mod to use a known good source type, such as: {2}",
                  clientModReport.SourceType, clientModReport.Name, string.Join(", ", allowList.GetAllowedSourceTypesForMod(clientModReport.Id))
                ));
                break;
              case AllowList.ProblemKind.UnrecognizedFingerprint:
                modIssuesForClient.Add(Lang.Get("Unrecognized or banned fingerprint for mod \"{0}\" — please update this mod with a freshly downloaded copy.", clientModReport.Name));
                break;
              default:
                throw new ApplicationException("Bad logic!");
            }
          }
        }
        if (unrecognizedModReportList.Count > 0) {
          recentUnrecognizedModReportsByPlayerUid.Add(player.PlayerUID, unrecognizedModReportList);
          var unrecognizedModList = unrecognizedModReportList.Select((modReport) => modReport.Name).Join();
          // cache these values, because disconnecting seems to wipe them
          var playerName = player.PlayerName;
          var playerUID = player.PlayerUID;
          DisconnectPlayerWithFriendlyMessage(player, Lang.Get("ModIntegrity: problems were found with your mods:\n\n{0}\n\n" + config.ExtraDisconnectMessage, string.Join("\n", modIssuesForClient)));
          sapi.Logger.Event($"ModIntegrity kicked {playerName} ({playerUID}) for the following unrecognized mod(s):");
          foreach (var modReport in unrecognizedModReportList) {
            sapi.Logger.Event($"[ModIntegrity] - ({modReport.SourceType}) \"{modReport.Name}\" ({modReport.Id}) version {modReport.Version}, filename {modReport.FileName}, md5 checksum {modReport.Fingerprint}");
          }
          sapi.Logger.Event($"To add all of the above mod fingerprints to the ModIntegrity allow list, trusting that {playerName}'s versions are untampered-with, type:");
          sapi.Logger.Event($"/modintegrityapprove {playerUID}");
        }
        else {
          // player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("ModIntegrity: Thank you, mods have been verified."), EnumChatType.Notification);
        }
      });
      // admin can use `/modintegrityapprove` to add all the mod fingerprints someone was last kicked for
      sapi.RegisterCommand(
        "modintegrityapprove",
        "Approves all mod fingerprints a player was recently kicked for",
        "/modintegrityapprove PlayerUID",
        (IServerPlayer player, int groupId, CmdArgs args) => {
          var playerUid = args.PopWord();
          if (recentUnrecognizedModReportsByPlayerUid.TryGetValue(playerUid, out var modReportList)) {
            foreach (var modReport in modReportList) {
              config.AllowedClientOnlyMods = config.AllowedClientOnlyMods.AddToArray(modReport); // permanently add to config
              allowList.AddModReport(modReport); // also add to cache so they can login before next server reload
            }
            ModConfig.Save(sapi, config);
            sapi.Logger.Event($"ModIntegrity: added {modReportList.Count} entries to the AllowedClientOnlyMods mod config");
          }
          else {
            sapi.Logger.Error($"ModIntegrity: could not find recentUnrecognizedModReportsByPlayerUid for \"{playerUid}\"");
          }
        },
        Privilege.root
      );
      sapi.RegisterCommand(
        "modintegritylongestgrace",
        "TEMPORARY: shows longest grace time required for a Join (for this session)",
        "/modintegritylongestgrace",
        (IServerPlayer player, int groupId, CmdArgs args) => {
          player.SendMessage(GlobalConstants.GeneralChatGroup, $"ModIntegrity: longestGraceRequired = {TEMPlongestGraceRequired} ms", EnumChatType.Notification);
        },
        Privilege.chat
      );
    }
    private void DisconnectPlayerWithFriendlyMessage(IServerPlayer player, string message) {
      // var type = player.GetType();
      // var bindingFlags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
      ServerMain server = player.XXX_GetFieldValue<ServerMain>("server"); // type.GetField("server", bindingFlags).GetValue(player) as ServerMain;
      ConnectedClient client = player.XXX_GetFieldValue<ConnectedClient>("client"); // type.GetField("client", bindingFlags).GetValue(player) as ConnectedClient;
      server.DisconnectPlayer(client, message, message);
    }
  }

  // [HarmonyPatch(typeof(ServerMain))]
  // [HarmonyPatch("HandlePlayerIdentification")]
  // public class Patch_ServerMain_HandlePlayerIdentification {
  //   public static IServerNetworkChannel NetworkChannel;
  //   public static void Postfix(ServerMain __instance, ConnectedClient client) {
  //     NetworkApiModIntegrityPacket payload = new NetworkApiModIntegrityPacket(); // any packet will do! this is an empty list. should probably use a new class instead of reusing this one
  //     var message = NetworkChannel.XXX_GetMethod("GenPacket").MakeGenericMethod(new Type[] { payload.GetType() }).Invoke(NetworkChannel, new object[] { payload }) as byte[];
  //     var socket = client.XXX_GetFieldValue<NetConnection>("Socket");
  //     socket.Send(message, false);
  //     }
  // }
}
