using ProtoBuf;
using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using System.Security.Cryptography;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace ModIntegrity {
  class Md5Tools {
    public static string md5Mod(Mod mod) {
      return byteArrayToHexString(mod.SourceType == EnumModSourceType.Folder ? md5Folder(mod.SourcePath) : md5File(mod.SourcePath));
    }
    private static string byteArrayToHexString(byte[] byteArray) {
      return BitConverter.ToString(byteArray).Replace("-", "");
    }
    private static byte[] md5File(string filePath) {
      using (var md5 = MD5.Create()) {
        using (var stream = File.OpenRead(filePath)) {
          return md5.ComputeHash(stream);
        }
      }
    }
    private static byte[] md5Folder(string folderPath) {
      // get all files, including from nested subdirs
      var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories).OrderBy(p => p).ToList();
      MD5 md5 = MD5.Create();
      for (int i = 0; i < files.Count; i++) {
        string file = files[i];

        // hash path
        string relativePath = file.Substring(folderPath.Length + 1);
        byte[] pathBytes = Encoding.UTF8.GetBytes(relativePath.ToLower());
        md5.TransformBlock(pathBytes, 0, pathBytes.Length, pathBytes, 0);

        // hash contents
        byte[] contentBytes = File.ReadAllBytes(file);
        if (i == files.Count - 1)
          md5.TransformFinalBlock(contentBytes, 0, contentBytes.Length);
        else
          md5.TransformBlock(contentBytes, 0, contentBytes.Length, contentBytes, 0);
      }
      return md5.Hash;
    }
  }
}