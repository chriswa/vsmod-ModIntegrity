using System.Collections.Generic;
using System.Linq;

namespace ModIntegrity {
  class AllowList {
    private Dictionary<string, List<ModReport>> allowedModReportsByModId = new Dictionary<string, List<ModReport>>();
    public void AddModReport(ModReport modReport) {
      if (!allowedModReportsByModId.ContainsKey(modReport.Id)) {
        allowedModReportsByModId.Add(modReport.Id, new List<ModReport>());
      }
      allowedModReportsByModId[modReport.Id].Add(modReport);
    }
    public enum ProblemKind {
      None,
      UnrecognizedModId,
      UnrecognizedVersion,
      UnrecognizedSourceType,
      UnrecognizedFingerprint
    }
    public ProblemKind GetClientModReportProblem(ModReport clientModReport) {
      bool foundModId = allowedModReportsByModId.TryGetValue(clientModReport.Id, out List<ModReport> allowedModReportList);
      if (foundModId) {
        bool foundMatchingVersion = false;
        bool foundMatchingSourceType = false;
        foreach (var allowedModReport in allowedModReportList) {
          if (allowedModReport.Fingerprint == clientModReport.Fingerprint) {
            return ProblemKind.None;
          }
          if (allowedModReport.Version == clientModReport.Version) {
            foundMatchingVersion = true;
          }
          if (allowedModReport.SourceType == clientModReport.SourceType) {
            foundMatchingSourceType = true;
          }
        }
        if (!foundMatchingVersion) {
          return ProblemKind.UnrecognizedVersion;
        }
        else if (!foundMatchingSourceType) {
          return ProblemKind.UnrecognizedSourceType;
        }
        else {
          return ProblemKind.UnrecognizedFingerprint;
        }
      }
      return ProblemKind.UnrecognizedModId;
    }
    public IEnumerable<string> GetAllowedVersionsForMod(string modId) {
      bool foundModId = allowedModReportsByModId.TryGetValue(modId, out List<ModReport> allowedModReportList);
      if (foundModId) {
        return allowedModReportList.Select((allowedModReport) => allowedModReport.Version).Distinct();
      }
      return Enumerable.Empty<string>();
    }
    public IEnumerable<string> GetAllowedSourceTypesForMod(string modId) {
      HashSet<string> result = new HashSet<string>();
      bool foundModId = allowedModReportsByModId.TryGetValue(modId, out List<ModReport> allowedModReportList);
      if (foundModId) {
        return allowedModReportList.Select((allowedModReport) => allowedModReport.SourceType).Distinct();
      }
      return Enumerable.Empty<string>();
    }
  }
}